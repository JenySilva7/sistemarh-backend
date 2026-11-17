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

    private static FuncionarioCreateDto CriarDtoValido(int departamentoId, int cargoId, string cpf = "526.018.159-06") => new()
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
        await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "083.016.613-05"));

        var dtoDuplicado = CriarDtoValido(departamentoId, cargoId, cpf: "083.016.613-05");
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
        var funcionarioA = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "186.091.390-34"));
        await service.DefinirAcessoAppAsync(funcionarioA.Id, "senha123");
        await service.AtualizarAsync(funcionarioA.Id, new FuncionarioUpdateDto
        {
            Nome = funcionarioA.Nome,
            Email = "novo.email@empresa.com",
            DepartamentoId = departamentoId,
            CargoId = cargoId,
        });
        var funcionarioB = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "996.030.824-30"));

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
        var funcionarioA = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "186.091.390-34"));
        db.Usuarios.Add(new Usuario { Nome = "Conta de A", Email = "ana.souza@empresa.com", FuncionarioId = funcionarioA.Id });
        await service.AtualizarAsync(funcionarioA.Id, new FuncionarioUpdateDto
        {
            Nome = funcionarioA.Nome,
            Email = "outro@empresa.com",
            DepartamentoId = departamentoId,
            CargoId = cargoId,
        });
        var dto = CriarDtoValido(departamentoId, cargoId, cpf: "996.030.824-30");
        dto.SenhaAcessoApp = "senha123";

        await Assert.ThrowsAsync<ConflictException>(() => service.CriarAsync(dto));

        Assert.Single(db.Funcionarios);
    }

    [Fact]
    public async Task Listar_IndicaQuemPossuiAcessoApp()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var comAcesso = CriarDtoValido(departamentoId, cargoId, cpf: "186.091.390-34");
        comAcesso.SenhaAcessoApp = "senha123";
        var semAcesso = CriarDtoValido(departamentoId, cargoId, cpf: "996.030.824-30");
        semAcesso.Nome = "Bruno Lima";
        semAcesso.Email = "bruno@empresa.com";
        await service.CriarAsync(comAcesso);
        await service.CriarAsync(semAcesso);

        var lista = await service.ListarAsync(null, null);

        Assert.True(lista.Single(f => f.Nome == "Ana Souza").PossuiAcessoApp);
        Assert.False(lista.Single(f => f.Nome == "Bruno Lima").PossuiAcessoApp);
    }

    // ── Remuneração (salário, vale alimentação e promoção) ──────────────

    private static readonly DateTime Vigencia = new(2026, 12, 1);

    private static Usuario CriarResponsavel(AppDbContext db)
    {
        var usuario = new Usuario { Nome = "Equipe RH", Email = "rh@empresa.com", Perfil = PerfilUsuario.Rh };
        db.Usuarios.Add(usuario);
        db.SaveChanges();
        return usuario;
    }

    /// <summary>Cargo e departamento de destino de uma promoção.</summary>
    private static (int departamentoId, int cargoId) CriarDestinoDaPromocao(AppDbContext db)
    {
        var departamento = new Departamento { Nome = "Diretoria", Descricao = "Diretoria" };
        var cargo = new Cargo { Nome = "Tech Lead", SalarioBase = 9000m, Descricao = "Liderança técnica" };
        db.Departamentos.Add(departamento);
        db.Cargos.Add(cargo);
        db.SaveChanges();
        return (departamento.Id, cargo.Id);
    }

    private static AjusteSalarioDto AjusteSalario(decimal? novoSalario = 5500m, string motivo = "Reajuste anual") => new()
    {
        NovoSalario = novoSalario,
        DataVigencia = Vigencia,
        Motivo = motivo,
    };

    private static AjusteValeAlimentacaoDto AjusteVale(decimal? novoValor = 700m) => new()
    {
        NovoValor = novoValor,
        DataVigencia = Vigencia,
        Motivo = "Novo acordo coletivo",
    };

    private static PromocaoDto Promocao(int cargoId, int departamentoId, decimal? novoSalario = 9500m) => new()
    {
        NovoCargoId = cargoId,
        NovoDepartamentoId = departamentoId,
        NovoSalario = novoSalario,
        DataVigencia = Vigencia,
        Motivo = "Promoção a líder técnico",
    };

    [Fact]
    public async Task Criar_SemSalarioNemValeAlimentacao_UsaSalarioBaseDoCargoEValePadrao()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);

        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        Assert.Equal(5000m, funcionario.Salario);
        Assert.Equal(600m, funcionario.ValeAlimentacao);
        Assert.Equal(FuncionarioService.ValeAlimentacaoPadrao, db.Funcionarios.Single().ValeAlimentacao);
    }

    [Fact]
    public async Task Criar_ComSalarioEValeAlimentacao_UsaOsValoresInformados()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var dto = CriarDtoValido(departamentoId, cargoId);
        dto.Salario = 6200.50m;
        dto.ValeAlimentacao = 0m;

        var funcionario = await service.CriarAsync(dto);

        Assert.Equal(6200.50m, funcionario.Salario);
        Assert.Equal(0m, funcionario.ValeAlimentacao);
        Assert.Equal(6200.50m, db.Funcionarios.Single().Salario);
    }

    [Fact]
    public async Task AjustarSalario_AtualizaOSalarioEGravaNoHistorico()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var responsavel = CriarResponsavel(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var resultado = await service.AjustarSalarioAsync(funcionario.Id, AjusteSalario(5500m, "  Reajuste anual  "), responsavel.Id);

        Assert.Equal(5500m, resultado.Salario);
        Assert.Equal(600m, resultado.ValeAlimentacao);
        Assert.Equal(cargoId, resultado.CargoId);
        var ajuste = Assert.Single(db.AjustesRemuneracao);
        Assert.Equal(funcionario.Id, ajuste.FuncionarioId);
        Assert.Equal(TipoAjusteRemuneracao.Salario, ajuste.Tipo);
        Assert.Equal(5000m, ajuste.ValorAnterior);
        Assert.Equal(5500m, ajuste.ValorNovo);
        Assert.Equal(Vigencia, ajuste.DataVigencia);
        Assert.Equal("Reajuste anual", ajuste.Motivo);
        Assert.Equal(responsavel.Id, ajuste.ResponsavelUsuarioId);
        Assert.Null(ajuste.CargoNovoId);
        Assert.Null(ajuste.DepartamentoNovoId);
    }

    [Fact]
    public async Task AjustarValeAlimentacao_AtualizaOValorEGravaNoHistorico()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var responsavel = CriarResponsavel(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));
        var dto = AjusteVale(700m);
        dto.DataVigencia = new DateTime(2026, 12, 1, 15, 30, 0, DateTimeKind.Utc);

        var resultado = await service.AjustarValeAlimentacaoAsync(funcionario.Id, dto, responsavel.Id);

        Assert.Equal(700m, resultado.ValeAlimentacao);
        Assert.Equal(5000m, resultado.Salario);
        var ajuste = Assert.Single(db.AjustesRemuneracao);
        Assert.Equal(TipoAjusteRemuneracao.ValeAlimentacao, ajuste.Tipo);
        Assert.Equal(600m, ajuste.ValorAnterior);
        Assert.Equal(700m, ajuste.ValorNovo);
        Assert.Equal(responsavel.Id, ajuste.ResponsavelUsuarioId);
        // Data de calendário: fica só o dia, sem horário nem fuso.
        Assert.Equal(Vigencia, ajuste.DataVigencia);
        Assert.Equal(DateTimeKind.Unspecified, ajuste.DataVigencia.Kind);
    }

    [Fact]
    public async Task AjustarValeAlimentacao_ParaZero_EhPermitido()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var resultado = await service.AjustarValeAlimentacaoAsync(funcionario.Id, AjusteVale(0m), 1);

        Assert.Equal(0m, resultado.ValeAlimentacao);
        Assert.Single(db.AjustesRemuneracao);
    }

    [Fact]
    public async Task Promover_TrocaCargoDepartamentoESalarioEGravaNoHistorico()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var (novoDepartamentoId, novoCargoId) = CriarDestinoDaPromocao(db);
        var responsavel = CriarResponsavel(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var resultado = await service.PromoverAsync(funcionario.Id, Promocao(novoCargoId, novoDepartamentoId, 9500m), responsavel.Id);

        Assert.Equal(novoCargoId, resultado.CargoId);
        Assert.Equal("Tech Lead", resultado.CargoNome);
        Assert.Equal(novoDepartamentoId, resultado.DepartamentoId);
        Assert.Equal("Diretoria", resultado.DepartamentoNome);
        Assert.Equal(9500m, resultado.Salario);
        var salvo = db.Funcionarios.Single();
        Assert.Equal(novoCargoId, salvo.CargoId);
        Assert.Equal(novoDepartamentoId, salvo.DepartamentoId);

        var ajuste = Assert.Single(db.AjustesRemuneracao);
        Assert.Equal(TipoAjusteRemuneracao.Promocao, ajuste.Tipo);
        Assert.Equal(5000m, ajuste.ValorAnterior);
        Assert.Equal(9500m, ajuste.ValorNovo);
        Assert.Equal(cargoId, ajuste.CargoAnteriorId);
        Assert.Equal(novoCargoId, ajuste.CargoNovoId);
        Assert.Equal(departamentoId, ajuste.DepartamentoAnteriorId);
        Assert.Equal(novoDepartamentoId, ajuste.DepartamentoNovoId);
        Assert.Equal("Promoção a líder técnico", ajuste.Motivo);
        Assert.Equal(responsavel.Id, ajuste.ResponsavelUsuarioId);
    }

    [Fact]
    public async Task Promover_MudandoSoODepartamento_EhPermitido()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var (novoDepartamentoId, _) = CriarDestinoDaPromocao(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var resultado = await service.PromoverAsync(funcionario.Id, Promocao(cargoId, novoDepartamentoId, 5000m), 1);

        Assert.Equal(novoDepartamentoId, resultado.DepartamentoId);
        Assert.Equal(cargoId, resultado.CargoId);
        Assert.Equal(5000m, resultado.Salario);
    }

    [Fact]
    public async Task Promover_SemNenhumaMudanca_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.PromoverAsync(funcionario.Id, Promocao(cargoId, departamentoId, 5000.00m), 1));

        Assert.Equal("A promoção precisa mudar o cargo, o departamento ou o salário atual do colaborador.", erro.Message);
        Assert.Empty(db.AjustesRemuneracao);
    }

    [Theory]
    [InlineData(true, "Cargo informado não existe.")]
    [InlineData(false, "Departamento informado não existe.")]
    public async Task Promover_ComCargoOuDepartamentoInexistente_LancaNotFoundException(bool cargoInexistente, string mensagem)
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));
        var dto = cargoInexistente ? Promocao(999, departamentoId) : Promocao(cargoId, 999);

        var erro = await Assert.ThrowsAsync<NotFoundException>(() => service.PromoverAsync(funcionario.Id, dto, 1));

        Assert.Equal(mensagem, erro.Message);
        Assert.Empty(db.AjustesRemuneracao);
    }

    [Fact]
    public async Task AjustesDeRemuneracao_FuncionarioDesligado_LancamBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var (novoDepartamentoId, novoCargoId) = CriarDestinoDaPromocao(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));
        await service.DesligarAsync(funcionario.Id, new DesligarFuncionarioDto());

        var erros = new[]
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.AjustarSalarioAsync(funcionario.Id, AjusteSalario(), 1)),
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.AjustarValeAlimentacaoAsync(funcionario.Id, AjusteVale(), 1)),
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.PromoverAsync(funcionario.Id, Promocao(novoCargoId, novoDepartamentoId), 1)),
        };

        Assert.All(erros, erro => Assert.Equal("Não é possível ajustar a remuneração de um colaborador desligado.", erro.Message));
        Assert.Empty(db.AjustesRemuneracao);
        Assert.Equal(5000m, db.Funcionarios.Single().Salario);
    }

    [Fact]
    public async Task Remuneracao_FuncionarioInexistente_LancaNotFoundException()
    {
        var service = CriarServico(out _);

        await Assert.ThrowsAsync<NotFoundException>(() => service.AjustarSalarioAsync(999, AjusteSalario(), 1));
        await Assert.ThrowsAsync<NotFoundException>(() => service.AjustarValeAlimentacaoAsync(999, AjusteVale(), 1));
        await Assert.ThrowsAsync<NotFoundException>(() => service.PromoverAsync(999, Promocao(1, 1), 1));
        var erro = await Assert.ThrowsAsync<NotFoundException>(() => service.ListarAjustesRemuneracaoAsync(999));
        Assert.Equal("Funcionário não encontrado.", erro.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task AjustarSalario_ComSalarioZeroOuNegativo_LancaBusinessRuleException(int novoSalario)
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AjustarSalarioAsync(funcionario.Id, AjusteSalario(novoSalario), 1));

        Assert.Equal("O salário precisa ser maior que zero.", erro.Message);
        Assert.Empty(db.AjustesRemuneracao);
        Assert.Equal(5000m, db.Funcionarios.Single().Salario);
    }

    [Theory]
    [InlineData("", "Informe o motivo do ajuste.")]
    [InlineData("   ", "Informe o motivo do ajuste.")]
    [InlineData(" ab ", "O motivo precisa ter pelo menos 3 caracteres.")]
    public async Task AjustarSalario_ComMotivoVazioOuCurto_LancaBusinessRuleException(string motivo, string mensagem)
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AjustarSalarioAsync(funcionario.Id, AjusteSalario(motivo: motivo), 1));

        Assert.Equal(mensagem, erro.Message);
        Assert.Empty(db.AjustesRemuneracao);
    }

    [Fact]
    public async Task AjustarSalario_SemDataDeVigencia_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));
        var dto = AjusteSalario();
        dto.DataVigencia = null;

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AjustarSalarioAsync(funcionario.Id, dto, 1));

        Assert.Equal("Informe a data de vigência.", erro.Message);
    }

    [Fact]
    public async Task AjustarValeAlimentacao_ComValorNegativo_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AjustarValeAlimentacaoAsync(funcionario.Id, AjusteVale(-1m), 1));

        Assert.Equal("O vale alimentação não pode ser negativo.", erro.Message);
        Assert.Equal(600m, db.Funcionarios.Single().ValeAlimentacao);
    }

    [Fact]
    public async Task ListarAjustes_DevolveSoOsDoFuncionario_MaisRecentesPrimeiro_ComOsNomes()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var (novoDepartamentoId, novoCargoId) = CriarDestinoDaPromocao(db);
        var responsavel = CriarResponsavel(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "186.091.390-34"));
        var outro = CriarDtoValido(departamentoId, cargoId, cpf: "996.030.824-30");
        outro.Email = "outro@empresa.com";
        var outroFuncionario = await service.CriarAsync(outro);

        await service.AjustarSalarioAsync(funcionario.Id, AjusteSalario(5500m), responsavel.Id);
        await service.AjustarSalarioAsync(outroFuncionario.Id, AjusteSalario(8000m), responsavel.Id);
        await service.AjustarValeAlimentacaoAsync(funcionario.Id, AjusteVale(700m), responsavel.Id);
        await service.PromoverAsync(funcionario.Id, Promocao(novoCargoId, novoDepartamentoId, 9500m), responsavel.Id);

        var historico = await service.ListarAjustesRemuneracaoAsync(funcionario.Id);

        Assert.Equal(
            new[] { TipoAjusteRemuneracao.Promocao, TipoAjusteRemuneracao.ValeAlimentacao, TipoAjusteRemuneracao.Salario },
            historico.Select(a => a.Tipo));

        var promocao = historico[0];
        Assert.Equal(5500m, promocao.ValorAnterior);
        Assert.Equal(9500m, promocao.ValorNovo);
        Assert.Equal("Desenvolvedor", promocao.CargoAnteriorNome);
        Assert.Equal("Tech Lead", promocao.CargoNovoNome);
        Assert.Equal("Tecnologia", promocao.DepartamentoAnteriorNome);
        Assert.Equal("Diretoria", promocao.DepartamentoNovoNome);
        Assert.Equal(Vigencia, promocao.DataVigencia);
        Assert.Equal("Promoção a líder técnico", promocao.Motivo);
        Assert.Equal("Equipe RH", promocao.ResponsavelNome);

        var salario = historico[2];
        Assert.Equal(5000m, salario.ValorAnterior);
        Assert.Equal(5500m, salario.ValorNovo);
        Assert.Null(salario.CargoAnteriorNome);
        Assert.Null(salario.CargoNovoNome);
        Assert.Null(salario.DepartamentoAnteriorNome);
        Assert.Null(salario.DepartamentoNovoNome);
    }
}
