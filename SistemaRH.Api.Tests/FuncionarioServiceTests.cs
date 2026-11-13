using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class FuncionarioServiceTests
{
    private static FuncionarioService CriarServico(out AppDbContext db)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        return new FuncionarioService(db);
    }

    /// <summary>Cria um Departamento e um Cargo direto no banco de teste, pra satisfazer as FKs obrigatórias de Funcionario.</summary>
    private static (int departamentoId, int cargoId) CriarDepartamentoECargo(AppDbContext db)
    {
        var departamento = new Departamento { Nome = "Tecnologia", Descricao = "TI" };
        var cargo = new Cargo { Nome = "Desenvolvedor", SalarioBase = 5000m, Descricao = "Dev" };
        db.Departamentos.Add(departamento);
        db.Cargos.Add(cargo);
        db.SaveChanges();
        return (departamento.Id, cargo.Id);
    }

    private static FuncionarioCreateDto CriarDtoValido(int departamentoId, int cargoId, string cpf = "111.111.111-11") => new()
    {
        Nome = "Ana Souza",
        Cpf = cpf,
        Email = "ana.souza@empresa.com",
        DataAdmissao = new DateTime(2024, 1, 1),
        DepartamentoId = departamentoId,
        CargoId = cargoId,
    };

    [Fact]
    public async Task Criar_ComDadosValidos_SalvaComStatusAtivo()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);

        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        Assert.Equal(StatusFuncionario.Ativo, funcionario.Status);
        Assert.Equal("Tecnologia", funcionario.DepartamentoNome);
        Assert.Equal("Desenvolvedor", funcionario.CargoNome);
    }

    [Fact]
    public async Task Criar_ComCpfDuplicado_LancaConflictException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "222.222.222-22"));

        var dtoDuplicado = CriarDtoValido(departamentoId, cargoId, cpf: "222.222.222-22");
        dtoDuplicado.Email = "outro.email@empresa.com";

        await Assert.ThrowsAsync<ConflictException>(() => service.CriarAsync(dtoDuplicado));
    }

    [Fact]
    public async Task Criar_ComDepartamentoInexistente_LancaNotFoundException()
    {
        var service = CriarServico(out var db);
        var (_, cargoId) = CriarDepartamentoECargo(db);

        var dto = CriarDtoValido(departamentoId: 999, cargoId);

        await Assert.ThrowsAsync<NotFoundException>(() => service.CriarAsync(dto));
    }

    [Fact]
    public async Task Desligar_FuncionarioAtivo_AtualizaStatusEData()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var desligado = await service.DesligarAsync(funcionario.Id, new DesligarFuncionarioDto());

        Assert.Equal(StatusFuncionario.Desligado, desligado.Status);
        Assert.NotNull(desligado.DataDesligamento);
    }

    [Fact]
    public async Task Desligar_FuncionarioJaDesligado_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));
        await service.DesligarAsync(funcionario.Id, new DesligarFuncionarioDto());

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DesligarAsync(funcionario.Id, new DesligarFuncionarioDto()));
    }

    // ── Acesso ao app (vínculo Usuario ↔ Funcionario) ───────────────────

    private static Task<AuthResponseDto> Logar(AppDbContext db, string email, string senha) =>
        new AuthService(db, new JwtTokenServiceFalso()).LoginAsync(new LoginDto { Email = email, Senha = senha });

    [Fact]
    public async Task DefinirAcessoApp_SemContaExistente_CriaUsuarioVinculado()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var dto = CriarDtoValido(departamentoId, cargoId);
        dto.Email = "  Ana.Souza@Empresa.com ";
        var funcionario = await service.CriarAsync(dto);
        Assert.False(funcionario.PossuiAcessoApp);

        var resultado = await service.DefinirAcessoAppAsync(funcionario.Id, "senha123");

        Assert.True(resultado.PossuiAcessoApp);
        var usuario = Assert.Single(db.Usuarios);
        Assert.Equal(funcionario.Id, usuario.FuncionarioId);
        Assert.Equal("ana.souza@empresa.com", usuario.Email);
        Assert.Equal("Ana Souza", usuario.Nome);
        Assert.Equal(PerfilUsuario.Funcionario, usuario.Perfil);
        Assert.Equal("Desenvolvedor", usuario.Cargo);
        Assert.Equal("Tecnologia", usuario.Departamento);

        var login = await Logar(db, "ana.souza@empresa.com", "senha123");
        Assert.Equal(funcionario.Id, login.Usuario.FuncionarioId);
    }

    [Fact]
    public async Task DefinirAcessoApp_ComContaJaCadastradaComMesmoEmail_VinculaESobrescreveSenha()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var authService = new AuthService(db, new JwtTokenServiceFalso());
        var cadastro = await authService.RegistrarAsync(new RegisterDto { Nome = "Quem Se Cadastrou", Email = "ana.souza@empresa.com", Senha = "senhaAntiga" });
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        await service.DefinirAcessoAppAsync(funcionario.Id, "senhaDoRh");

        var usuario = Assert.Single(db.Usuarios);
        Assert.Equal(cadastro.Usuario.Id, usuario.Id);
        Assert.Equal(funcionario.Id, usuario.FuncionarioId);
        Assert.Equal(PerfilUsuario.Funcionario, usuario.Perfil);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Logar(db, "ana.souza@empresa.com", "senhaAntiga"));
        var login = await Logar(db, "ana.souza@empresa.com", "senhaDoRh");
        Assert.Equal(funcionario.Id, login.Usuario.FuncionarioId);
    }

    [Fact]
    public async Task DefinirAcessoApp_ComEmailDeContaRhOuAdmin_LancaConflictESenhaNaoMuda()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var authService = new AuthService(db, new JwtTokenServiceFalso());
        await authService.RegistrarAsync(new RegisterDto { Nome = "Outra Pessoa do RH", Email = "ana.souza@empresa.com", Senha = "senhaDoAdmin" });
        db.Usuarios.Single().Perfil = PerfilUsuario.Admin;
        await db.SaveChangesAsync();
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        await Assert.ThrowsAsync<ConflictException>(() => service.DefinirAcessoAppAsync(funcionario.Id, "senhaInvasora"));

        Assert.Null(Assert.Single(db.Usuarios).FuncionarioId);
        await Logar(db, "ana.souza@empresa.com", "senhaDoAdmin");
    }

    [Fact]
    public async Task DefinirAcessoApp_QuandoJaTemAcesso_SoRedefineASenha()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));
        await service.DefinirAcessoAppAsync(funcionario.Id, "primeiraSenha");

        await service.DefinirAcessoAppAsync(funcionario.Id, "segundaSenha");

        Assert.Single(db.Usuarios);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Logar(db, "ana.souza@empresa.com", "primeiraSenha"));
        await Logar(db, "ana.souza@empresa.com", "segundaSenha");
    }

    [Fact]
    public async Task DefinirAcessoApp_ComEmailVinculadoAOutroFuncionario_LancaConflictException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        // Funcionário A ganha acesso com x@, depois o RH troca o e-mail do
        // cadastro dele — a conta de login continua sendo x@ e vinculada a A.
        var funcionarioA = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "1"));
        await service.DefinirAcessoAppAsync(funcionarioA.Id, "senha123");
        await service.AtualizarAsync(funcionarioA.Id, new FuncionarioUpdateDto
        {
            Nome = funcionarioA.Nome,
            Email = "novo.email@empresa.com",
            DepartamentoId = departamentoId,
            CargoId = cargoId,
        });
        var funcionarioB = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "2"));

        var erro = await Assert.ThrowsAsync<ConflictException>(() => service.DefinirAcessoAppAsync(funcionarioB.Id, "senha456"));

        Assert.Equal("Esse e-mail já está vinculado ao acesso de outro colaborador.", erro.Message);
        Assert.Equal(funcionarioA.Id, Assert.Single(db.Usuarios).FuncionarioId);
    }

    [Fact]
    public async Task DefinirAcessoApp_FuncionarioDesligado_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));
        await service.DesligarAsync(funcionario.Id, new DesligarFuncionarioDto());

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DefinirAcessoAppAsync(funcionario.Id, "senha123"));

        Assert.Equal("Não é possível dar acesso ao app a um colaborador desligado.", erro.Message);
        Assert.Empty(db.Usuarios);
    }

    [Fact]
    public async Task DefinirAcessoApp_FuncionarioSemEmail_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var dto = CriarDtoValido(departamentoId, cargoId);
        dto.Email = null;
        var funcionario = await service.CriarAsync(dto);

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DefinirAcessoAppAsync(funcionario.Id, "senha123"));

        Assert.Equal("Cadastre um e-mail para esse colaborador antes de criar o acesso ao app.", erro.Message);
        Assert.Empty(db.Usuarios);
    }

    [Fact]
    public async Task DefinirAcessoApp_ComSenhaCurta_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DefinirAcessoAppAsync(funcionario.Id, "12345"));

        Assert.Equal("A senha de acesso ao app precisa ter pelo menos 6 caracteres.", erro.Message);
    }

    [Fact]
    public async Task DefinirAcessoApp_FuncionarioInexistente_LancaNotFoundException()
    {
        var service = CriarServico(out _);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DefinirAcessoAppAsync(999, "senha123"));
    }

    [Fact]
    public async Task Criar_ComSenhaAcessoApp_JaCriaOAcessoVinculado()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var dto = CriarDtoValido(departamentoId, cargoId);
        dto.SenhaAcessoApp = "senha123";

        var funcionario = await service.CriarAsync(dto);

        Assert.True(funcionario.PossuiAcessoApp);
        Assert.Equal(funcionario.Id, Assert.Single(db.Usuarios).FuncionarioId);
        await Logar(db, "ana.souza@empresa.com", "senha123");
    }

    [Fact]
    public async Task Criar_ComSenhaAcessoAppMasSemEmail_NaoCriaNada()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var dto = CriarDtoValido(departamentoId, cargoId);
        dto.Email = null;
        dto.SenhaAcessoApp = "senha123";

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(dto));

        Assert.Empty(db.Funcionarios);
        Assert.Empty(db.Usuarios);
    }

    [Fact]
    public async Task Criar_ComSenhaAcessoAppCurta_NaoCriaNada()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var dto = CriarDtoValido(departamentoId, cargoId);
        dto.SenhaAcessoApp = "123";

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(dto));

        Assert.Empty(db.Funcionarios);
    }

    [Fact]
    public async Task Criar_ComSenhaAcessoAppEEmailVinculadoAOutro_NaoCriaNada()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionarioA = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "1"));
        db.Usuarios.Add(new Usuario { Nome = "Conta de A", Email = "ana.souza@empresa.com", FuncionarioId = funcionarioA.Id });
        await service.AtualizarAsync(funcionarioA.Id, new FuncionarioUpdateDto
        {
            Nome = funcionarioA.Nome,
            Email = "outro@empresa.com",
            DepartamentoId = departamentoId,
            CargoId = cargoId,
        });
        var dto = CriarDtoValido(departamentoId, cargoId, cpf: "2");
        dto.SenhaAcessoApp = "senha123";

        await Assert.ThrowsAsync<ConflictException>(() => service.CriarAsync(dto));

        Assert.Single(db.Funcionarios);
    }

    [Fact]
    public async Task Listar_IndicaQuemPossuiAcessoApp()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var comAcesso = CriarDtoValido(departamentoId, cargoId, cpf: "1");
        comAcesso.SenhaAcessoApp = "senha123";
        var semAcesso = CriarDtoValido(departamentoId, cargoId, cpf: "2");
        semAcesso.Nome = "Bruno Lima";
        semAcesso.Email = "bruno@empresa.com";
        await service.CriarAsync(comAcesso);
        await service.CriarAsync(semAcesso);

        var lista = await service.ListarAsync(null, null);

        Assert.True(lista.Single(f => f.Nome == "Ana Souza").PossuiAcessoApp);
        Assert.False(lista.Single(f => f.Nome == "Bruno Lima").PossuiAcessoApp);
    }
}
