using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>Dublê (fake) do serviço de JWT — os testes de AuthService não
/// precisam de um token de verdade, só que o método seja chamado.</summary>
public class JwtTokenServiceFalso : IJwtTokenService
{
    public string GerarToken(Usuario usuario) => $"token-falso-para-usuario-{usuario.Id}";
}

public class AuthServiceTests
{
    private static AuthService CriarServico(out Data.AppDbContext db)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        return new AuthService(db, new JwtTokenServiceFalso());
    }

    [Fact]
    public async Task Registrar_ComDadosValidos_CriaUsuarioEDevolveToken()
    {
        var service = CriarServico(out _);

        var resultado = await service.RegistrarAsync(new RegisterDto
        {
            Nome = "Ana Silva",
            Email = "ana@empresa.com",
            Senha = "senha123",
        });

        Assert.NotEmpty(resultado.Token);
        Assert.Equal("ana@empresa.com", resultado.Usuario.Email);
    }

    [Fact]
    public async Task Registrar_ComEmailJaExistente_LancaConflictException()
    {
        var service = CriarServico(out _);
        await service.RegistrarAsync(new RegisterDto { Nome = "Ana", Email = "ana@empresa.com", Senha = "senha123" });

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RegistrarAsync(new RegisterDto { Nome = "Outra Ana", Email = "ana@empresa.com", Senha = "outrasenha" }));
    }

    [Fact]
    public async Task Login_ComSenhaCorreta_DevolveToken()
    {
        var service = CriarServico(out _);
        await service.RegistrarAsync(new RegisterDto { Nome = "Ana", Email = "ana@empresa.com", Senha = "senha123" });

        var resultado = await service.LoginAsync(new LoginDto { Email = "ana@empresa.com", Senha = "senha123" });

        Assert.NotEmpty(resultado.Token);
    }

    [Fact]
    public async Task Login_ComSenhaIncorreta_LancaBusinessRuleException()
    {
        var service = CriarServico(out _);
        await service.RegistrarAsync(new RegisterDto { Nome = "Ana", Email = "ana@empresa.com", Senha = "senha123" });

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.LoginAsync(new LoginDto { Email = "ana@empresa.com", Senha = "senhaErrada" }));
    }

    [Fact]
    public async Task Login_ComUsuarioInexistente_LancaBusinessRuleException()
    {
        var service = CriarServico(out _);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.LoginAsync(new LoginDto { Email = "naoexiste@empresa.com", Senha = "qualquer" }));
    }

    [Fact]
    public async Task ObterUsuario_ComIdInexistente_LancaNotFoundException()
    {
        var service = CriarServico(out _);

        await Assert.ThrowsAsync<NotFoundException>(() => service.ObterUsuarioAsync(999));
    }

    // ── Conta vinculada a um funcionário ─────────────────────────────────

    /// <summary>Cria gestor + funcionário (com acesso ao app "carla@empresa.com" / "senha123") e devolve o Id do funcionário.</summary>
    private static async Task<int> CriarFuncionarioComAcesso(Data.AppDbContext db)
    {
        var departamento = new Departamento { Nome = "Financeiro" };
        var cargo = new Cargo { Nome = "Analista Financeiro" };
        var gerente = new Funcionario { Nome = "Roberto Gestor", Cpf = "0", DataAdmissao = new DateTime(2020, 1, 1), Departamento = departamento, Cargo = cargo };
        db.Funcionarios.Add(gerente);
        await db.SaveChangesAsync();

        var funcionarioService = new FuncionarioService(db);
        var funcionario = await funcionarioService.CriarAsync(new FuncionarioCreateDto
        {
            Nome = "Carla Dias",
            Cpf = "1",
            Email = "carla@empresa.com",
            DataAdmissao = new DateTime(2025, 3, 10),
            DepartamentoId = departamento.Id,
            CargoId = cargo.Id,
            GerenteId = gerente.Id,
            SenhaAcessoApp = "senha123",
        });
        return funcionario.Id;
    }

    [Fact]
    public async Task Login_ContaVinculada_DevolveDadosDoCadastroDoFuncionario()
    {
        var service = CriarServico(out var db);
        var funcionarioId = await CriarFuncionarioComAcesso(db);
        // Valores antigos na própria conta não devem prevalecer sobre o cadastro do RH.
        var conta = db.Usuarios.Single();
        conta.Cargo = "Cargo antigo";
        conta.Departamento = "Departamento antigo";
        await db.SaveChangesAsync();

        var resultado = await service.LoginAsync(new LoginDto { Email = "carla@empresa.com", Senha = "senha123" });

        Assert.Equal(funcionarioId, resultado.Usuario.FuncionarioId);
        Assert.Equal("Analista Financeiro", resultado.Usuario.Cargo);
        Assert.Equal("Financeiro", resultado.Usuario.Departamento);
        Assert.Equal(new DateTime(2025, 3, 10), resultado.Usuario.DataAdmissao);
        Assert.Equal("Roberto Gestor", resultado.Usuario.GerenteNome);
    }

    [Fact]
    public async Task Login_ContaSemVinculo_CamposDoFuncionarioFicamNulos()
    {
        var service = CriarServico(out _);
        await service.RegistrarAsync(new RegisterDto { Nome = "Ana", Email = "ana@empresa.com", Senha = "senha123", Cargo = "Dev", Departamento = "TI" });

        var resultado = await service.LoginAsync(new LoginDto { Email = "ana@empresa.com", Senha = "senha123" });

        Assert.Equal("Dev", resultado.Usuario.Cargo);
        Assert.Equal("TI", resultado.Usuario.Departamento);
        Assert.Null(resultado.Usuario.FuncionarioId);
        Assert.Null(resultado.Usuario.DataAdmissao);
        Assert.Null(resultado.Usuario.GerenteNome);
    }

    [Fact]
    public async Task Login_DeFuncionarioDesligado_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var funcionarioId = await CriarFuncionarioComAcesso(db);
        await new FuncionarioService(db).DesligarAsync(funcionarioId, new DesligarFuncionarioDto());

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.LoginAsync(new LoginDto { Email = "carla@empresa.com", Senha = "senha123" }));

        Assert.Equal("Seu acesso foi desativado. Procure o RH.", erro.Message);
    }

    [Fact]
    public async Task Login_DeFuncionarioDesligadoComSenhaErrada_NaoRevelaQueFoiDesativado()
    {
        var service = CriarServico(out var db);
        var funcionarioId = await CriarFuncionarioComAcesso(db);
        await new FuncionarioService(db).DesligarAsync(funcionarioId, new DesligarFuncionarioDto());

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.LoginAsync(new LoginDto { Email = "carla@empresa.com", Senha = "senhaErrada" }));

        Assert.Equal("E-mail ou senha incorretos.", erro.Message);
    }

    [Fact]
    public async Task ObterUsuario_DeFuncionarioDesligado_LancaForbiddenException()
    {
        var service = CriarServico(out var db);
        var funcionarioId = await CriarFuncionarioComAcesso(db);
        var usuarioId = db.Usuarios.Single().Id;
        await new FuncionarioService(db).DesligarAsync(funcionarioId, new DesligarFuncionarioDto());

        var erro = await Assert.ThrowsAsync<ForbiddenException>(() => service.ObterUsuarioAsync(usuarioId));

        Assert.Equal("Seu acesso foi desativado. Procure o RH.", erro.Message);
    }
}
