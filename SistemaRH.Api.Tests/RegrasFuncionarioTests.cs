using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Regras novas do cadastro de funcionários: gestor (ativo, sem ciclo),
/// desligamento com realocação da equipe, promoção, versão otimista e as
/// faixas de valores e datas.
/// </summary>
public class RegrasFuncionarioTests
{
    private static readonly DateTime Ontem = TestUtils.Hoje.AddDays(-1);

    private static FuncionarioService CriarServico(out AppDbContext db, string? nomeDoBanco = null)
    {
        db = nomeDoBanco is null ? TestUtils.CriarDbContextEmMemoria() : TestUtils.CriarDbContextEmMemoria(nomeDoBanco);
        return new FuncionarioService(db);
    }

    private static (int DepartamentoId, int CargoId) CriarDepartamentoECargo(AppDbContext db, string nome)
    {
        var departamento = new Departamento { Nome = nome };
        var cargo = new Cargo { Nome = "Cargo " + nome, SalarioBase = 5000m };
        db.AddRange(departamento, cargo);
        db.SaveChanges();
        return (departamento.Id, cargo.Id);
    }

    private static Task<FuncionarioDto> Criar(FuncionarioService service, (int DepartamentoId, int CargoId) lugar, string nome, int? gerenteId = null, string? email = null) =>
        service.CriarAsync(new FuncionarioCreateDto
        {
            Nome = nome,
            Cpf = TestUtils.GerarCpfValido(),
            Email = email,
            DataAdmissao = new DateTime(2024, 1, 1),
            DepartamentoId = lugar.DepartamentoId,
            CargoId = lugar.CargoId,
            GerenteId = gerenteId,
        });

    private static FuncionarioUpdateDto Edicao(FuncionarioDto f, int? gerenteId) => new()
    {
        Nome = f.Nome,
        Email = f.Email,
        DepartamentoId = f.DepartamentoId,
        CargoId = f.CargoId,
        GerenteId = gerenteId,
    };

    // ── Gestor ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Gestor_QueEstaAbaixoDoColaborador_FormaCicloERecusado()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var a = await Criar(service, ti, "Alice Alves");
        var b = await Criar(service, ti, "Bruno Borges", gerenteId: a.Id);
        var c = await Criar(service, ti, "Carla Costa", gerenteId: b.Id);

        // A gestor de B e B gestor de A; e o ciclo de 3 (A -> C -> B -> A).
        var direto = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AtualizarAsync(a.Id, Edicao(a, b.Id)));
        var deTres = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AtualizarAsync(a.Id, Edicao(a, c.Id)));

        Assert.Equal("Esse gestor está abaixo deste colaborador na hierarquia: a escolha criaria um ciclo de gestores.", direto.Message);
        Assert.Equal(direto.Message, deTres.Message);
        Assert.Null(db.Funcionarios.AsNoTracking().Single(f => f.Id == a.Id).GerenteId);
    }

    [Fact]
    public async Task Gestor_DesligadoOuInexistenteOuEleMesmo_EhRecusado()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var gestor = await Criar(service, ti, "Gestora Saindo");
        await service.DesligarAsync(gestor.Id, null);
        var colaborador = await Criar(service, ti, "Colaborador Ativo");

        var desligado = await Assert.ThrowsAsync<BusinessRuleException>(() => Criar(service, ti, "Novato Silva", gerenteId: gestor.Id));
        var inexistente = await Assert.ThrowsAsync<NotFoundException>(() => service.AtualizarAsync(colaborador.Id, Edicao(colaborador, 999)));
        var eleMesmo = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AtualizarAsync(colaborador.Id, Edicao(colaborador, colaborador.Id)));

        Assert.Equal("O gestor escolhido está desligado. Escolha um gestor ativo.", desligado.Message);
        Assert.Equal("Gestor informado não existe.", inexistente.Message);
        Assert.Equal("Um funcionário não pode ser gestor de si mesmo.", eleMesmo.Message);
    }

    [Fact]
    public async Task Gestor_DeOutroDepartamento_EhPermitido()
    {
        // A Diretoria gerencia os outros departamentos.
        var service = CriarServico(out var db);
        var diretoria = CriarDepartamentoECargo(db, "Diretoria");
        var ti = CriarDepartamentoECargo(db, "TI");
        var ceo = await Criar(service, diretoria, "Diretora Geral");

        var cto = await Criar(service, ti, "Diretor de TI", gerenteId: ceo.Id);

        Assert.Equal(ceo.Id, cto.GerenteId);
    }

    // ── Desligamento ────────────────────────────────────────────────────

    [Fact]
    public async Task Desligar_MoveOsSubordinadosParaOGestorDeQuemSai()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var ceo = await Criar(service, ti, "Diretora Geral");
        var gestora = await Criar(service, ti, "Gestora Intermediária", gerenteId: ceo.Id);
        var sub1 = await Criar(service, ti, "Subordinado Um", gerenteId: gestora.Id);
        var sub2 = await Criar(service, ti, "Subordinado Dois", gerenteId: gestora.Id);

        var resultado = await service.DesligarAsync(gestora.Id, new DesligarFuncionarioDto { DataDesligamento = Ontem });

        Assert.Equal(2, resultado.SubordinadosRealocados);
        Assert.Equal("Diretora Geral", resultado.NovoGestorNome);
        Assert.Equal(Ontem, resultado.Funcionario.DataDesligamento);
        Assert.All(db.Funcionarios.Where(f => f.Id == sub1.Id || f.Id == sub2.Id), f => Assert.Equal(ceo.Id, f.GerenteId));
    }

    [Fact]
    public async Task Desligar_ComGestorTambemDesligado_DeixaOsSubordinadosSemGestor()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var ceo = await Criar(service, ti, "Diretora Geral");
        var gestora = await Criar(service, ti, "Gestora Intermediária", gerenteId: ceo.Id);
        var sub = await Criar(service, ti, "Subordinado Um", gerenteId: gestora.Id);
        // Simula dado antigo: o gestor da gestora já estava desligado.
        db.Funcionarios.Single(f => f.Id == ceo.Id).Status = StatusFuncionario.Desligado;
        await db.SaveChangesAsync();

        var resultado = await service.DesligarAsync(gestora.Id, null);

        Assert.Equal(1, resultado.SubordinadosRealocados);
        Assert.Null(resultado.NovoGestorNome);
        Assert.Null(db.Funcionarios.Single(f => f.Id == sub.Id).GerenteId);
    }

    [Fact]
    public async Task Desligar_ComCicloAntigoNoBanco_NaoCriaAutoGestao()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var a = await Criar(service, ti, "Alice Alves");
        var b = await Criar(service, ti, "Bruno Borges", gerenteId: a.Id);
        // Ciclo gravado antes da validação existir: A -> B -> A.
        db.Funcionarios.Single(f => f.Id == a.Id).GerenteId = b.Id;
        await db.SaveChangesAsync();

        var resultado = await service.DesligarAsync(a.Id, null);

        Assert.Equal(1, resultado.SubordinadosRealocados);
        Assert.Null(resultado.NovoGestorNome);
        Assert.Null(db.Funcionarios.Single(f => f.Id == b.Id).GerenteId);
    }

    [Theory]
    [InlineData(1)]       // amanhã
    [InlineData(-10000)]  // antes da admissão
    public async Task Desligar_ComDataForaDaFaixa_Lanca400(int dias)
    {
        var service = CriarServico(out var db);
        var f = await Criar(service, CriarDepartamentoECargo(db, "TI"), "Pessoa Teste");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DesligarAsync(f.Id, new DesligarFuncionarioDto { DataDesligamento = TestUtils.Hoje.AddDays(dias) }));

        Assert.Equal(StatusFuncionario.Ativo, db.Funcionarios.Single().Status);
    }

    [Fact]
    public async Task Desligar_SobeAVersaoDoTokenDaConta()
    {
        var service = CriarServico(out var db);
        var f = await Criar(service, CriarDepartamentoECargo(db, "TI"), "Pessoa Teste", email: "pessoa@empresa.com");
        await service.DefinirAcessoAppAsync(f.Id, "senha123");
        var antes = db.Usuarios.Single().TokenVersao;

        await service.DesligarAsync(f.Id, null);

        Assert.Equal(antes + 1, db.Usuarios.Single().TokenVersao);
    }

    // ── Promoção ────────────────────────────────────────────────────────

    private static PromocaoDto Promocao(int cargoId, int departamentoId, decimal salario, int? novoGerenteId = null) => new()
    {
        NovoCargoId = cargoId,
        NovoDepartamentoId = departamentoId,
        NovoSalario = salario,
        DataVigencia = Ontem,
        Motivo = "Promoção",
        NovoGerenteId = novoGerenteId,
    };

    [Fact]
    public async Task Promover_SoComSalario_PedeAjusteSalarial()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var f = await Criar(service, ti, "Pessoa Teste");

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.PromoverAsync(f.Id, Promocao(ti.CargoId, ti.DepartamentoId, 9000m), 1));

        Assert.Equal("Para alterar só o salário, use Ajuste Salarial.", erro.Message);
    }

    [Fact]
    public async Task Promover_ReduzindoOSalario_Lanca400()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var vendas = CriarDepartamentoECargo(db, "Vendas");
        var f = await Criar(service, ti, "Pessoa Teste");

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.PromoverAsync(f.Id, Promocao(vendas.CargoId, ti.DepartamentoId, 4999.99m), 1));

        Assert.Equal("Promoção não pode reduzir o salário.", erro.Message);
        Assert.Empty(db.AjustesRemuneracao);
    }

    [Fact]
    public async Task Promover_ParaOutroDepartamento_DeixaAEquipeAntigaComOGestorAnterior()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var diretoria = CriarDepartamentoECargo(db, "Diretoria");
        var cto = await Criar(service, ti, "Diretor de TI");
        var lider = await Criar(service, ti, "Líder Técnica", gerenteId: cto.Id);
        var dev1 = await Criar(service, ti, "Dev Um", gerenteId: lider.Id);
        var dev2 = await Criar(service, ti, "Dev Dois", gerenteId: lider.Id);
        var ceo = await Criar(service, diretoria, "Diretora Geral");

        var resultado = await service.PromoverAsync(lider.Id, Promocao(diretoria.CargoId, diretoria.DepartamentoId, 9000m, novoGerenteId: ceo.Id), 1);

        Assert.Equal(2, resultado.SubordinadosRealocados);
        Assert.Equal(diretoria.DepartamentoId, resultado.Funcionario.DepartamentoId);
        Assert.Equal(ceo.Id, resultado.Funcionario.GerenteId);
        Assert.All(db.Funcionarios.Where(f => f.Id == dev1.Id || f.Id == dev2.Id), f => Assert.Equal(cto.Id, f.GerenteId));
    }

    [Fact]
    public async Task Promover_MesmoDepartamento_MantemAEquipe()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var senior = new Cargo { Nome = "Sênior", SalarioBase = 9000m };
        db.Cargos.Add(senior);
        db.SaveChanges();
        var lider = await Criar(service, ti, "Líder Técnica");
        var dev = await Criar(service, ti, "Dev Um", gerenteId: lider.Id);

        var resultado = await service.PromoverAsync(lider.Id, Promocao(senior.Id, ti.DepartamentoId, 9000m), 1);

        Assert.Equal(0, resultado.SubordinadosRealocados);
        Assert.Equal(lider.Id, db.Funcionarios.Single(f => f.Id == dev.Id).GerenteId);
    }

    [Fact]
    public async Task Promover_ComNovoGestorQueFormariaCiclo_Lanca400()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        var vendas = CriarDepartamentoECargo(db, "Vendas");
        var lider = await Criar(service, ti, "Líder Técnica");
        var dev = await Criar(service, ti, "Dev Um", gerenteId: lider.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.PromoverAsync(lider.Id, Promocao(vendas.CargoId, ti.DepartamentoId, 6000m, novoGerenteId: dev.Id), 1));
    }

    // ── Remuneração: versão, valor igual, faixas ───────────────────────

    private static AjusteSalarioDto Salario(decimal valor, int? versao = null, DateTime? vigencia = null) => new()
    {
        NovoSalario = valor,
        DataVigencia = vigencia ?? Ontem,
        Motivo = "Reajuste",
        Versao = versao,
    };

    [Fact]
    public async Task AjustarSalario_ComVersaoDaTelaDesatualizada_Lanca409ENaoGrava()
    {
        // A tela foi aberta com R$ 5.000; outra pessoa subiu pra R$ 8.000; a
        // tela velha não pode "aplicar +10%" sobre o valor antigo.
        var service = CriarServico(out var db);
        var f = await Criar(service, CriarDepartamentoECargo(db, "TI"), "Pessoa Teste");
        await service.AjustarSalarioAsync(f.Id, Salario(8000m, versao: f.Versao), 1);

        var erro = await Assert.ThrowsAsync<ConflictException>(() => service.AjustarSalarioAsync(f.Id, Salario(5500m, versao: f.Versao), 1));

        Assert.Equal("Este registro foi alterado por outra pessoa. Atualize a tela e tente de novo.", erro.Message);
        Assert.Equal(8000m, db.Funcionarios.Single().Salario);
        Assert.Single(db.AjustesRemuneracao);
    }

    [Fact]
    public async Task AjustarSalario_DuasRequisicoesComOMesmoDado_ASegundaFalhaPorConcorrencia()
    {
        var nomeDoBanco = Guid.NewGuid().ToString();
        var service1 = CriarServico(out var db1, nomeDoBanco);
        var f = await Criar(service1, CriarDepartamentoECargo(db1, "TI"), "Pessoa Teste");
        var db2 = TestUtils.CriarDbContextEmMemoria(nomeDoBanco);
        var lidoPelaSegunda = db2.Funcionarios.Single(x => x.Id == f.Id);

        await service1.AjustarSalarioAsync(f.Id, Salario(6000m), 1);
        lidoPelaSegunda.Salario = 7000m;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Ajustes_ComValorIgualAoAtual_Lanca400()
    {
        var service = CriarServico(out var db);
        var f = await Criar(service, CriarDepartamentoECargo(db, "TI"), "Pessoa Teste");

        var salario = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AjustarSalarioAsync(f.Id, Salario(5000m), 1));
        var vale = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AjustarValeAlimentacaoAsync(f.Id,
            new AjusteValeAlimentacaoDto { NovoValor = 600m, DataVigencia = Ontem, Motivo = "Reajuste" }, 1));

        Assert.Equal("O novo valor é igual ao atual.", salario.Message);
        Assert.Equal("O novo valor é igual ao atual.", vale.Message);
        Assert.Empty(db.AjustesRemuneracao);
    }

    [Fact]
    public async Task AjustarSalario_ComVigenciaFuturaOuAntesDaAdmissao_Lanca400()
    {
        var service = CriarServico(out var db);
        var f = await Criar(service, CriarDepartamentoECargo(db, "TI"), "Pessoa Teste");

        var futura = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AjustarSalarioAsync(f.Id, Salario(6000m, vigencia: TestUtils.Hoje.AddDays(1)), 1));
        var antiga = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AjustarSalarioAsync(f.Id, Salario(6000m, vigencia: new DateTime(1, 1, 1)), 1));

        Assert.Equal("Ajustes com data futura ainda não são suportados.", futura.Message);
        Assert.Equal("A data de vigência não pode ser anterior à admissão (01/01/2024).", antiga.Message);
    }

    [Theory]
    [InlineData("2345.678", "Informe o salário com no máximo 2 casas decimais.")]
    [InlineData("10000000000000000", "O salário precisa estar entre R$ 1,00 e R$ 1.000.000,00.")]
    [InlineData("0.01", "O salário precisa estar entre R$ 1,00 e R$ 1.000.000,00.")]
    public async Task AjustarSalario_ValoresAbsurdos_Lanca400(string valor, string mensagem)
    {
        var service = CriarServico(out var db);
        var f = await Criar(service, CriarDepartamentoECargo(db, "TI"), "Pessoa Teste");

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AjustarSalarioAsync(f.Id, Salario(decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture)), 1));

        Assert.Equal(mensagem, erro.Message);
    }

    [Fact]
    public async Task Criar_ComDadosAbsurdos_Lanca400ENaoGrava()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");
        FuncionarioCreateDto Dto() => new()
        {
            Nome = "Pessoa Teste",
            Cpf = TestUtils.GerarCpfValido(),
            DataAdmissao = new DateTime(2024, 1, 1),
            DepartamentoId = ti.DepartamentoId,
            CargoId = ti.CargoId,
        };

        var semAdmissao = Dto();
        semAdmissao.DataAdmissao = null;
        var nascimentoFuturo = Dto();
        nascimentoFuturo.DataNascimento = TestUtils.Hoje.AddDays(1);
        var admissaoAntesDoNascimento = Dto();
        admissaoAntesDoNascimento.DataNascimento = new DateTime(2015, 1, 1);
        var nomeInvisivel = Dto();
        nomeInvisivel.Nome = "Ana\u202E Souza";
        var telefone = Dto();
        telefone.Telefone = "<script>";
        var vale = Dto();
        vale.ValeAlimentacao = 1_000_000_000_000_000m;
        var semDepartamento = Dto();
        semDepartamento.DepartamentoId = null;

        Assert.Equal("A data de admissão é obrigatória.", (await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(semAdmissao))).Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(nascimentoFuturo));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(admissaoAntesDoNascimento));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(nomeInvisivel));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(telefone));
        Assert.Equal("O vale alimentação precisa estar entre R$ 0,00 e R$ 10.000,00.", (await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(vale))).Message);
        Assert.Equal("Selecione o departamento.", (await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(semDepartamento))).Message);
        Assert.Empty(db.Funcionarios);
    }

    [Fact]
    public async Task Criar_ComSenhaSoDeEspacos_NaoCriaNada()
    {
        var service = CriarServico(out var db);
        var ti = CriarDepartamentoECargo(db, "TI");

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(new FuncionarioCreateDto
        {
            Nome = "Pessoa Teste",
            Cpf = TestUtils.GerarCpfValido(),
            Email = "pessoa@empresa.com",
            DataAdmissao = new DateTime(2024, 1, 1),
            DepartamentoId = ti.DepartamentoId,
            CargoId = ti.CargoId,
            SenhaAcessoApp = "      ",
        }));

        Assert.Equal("A senha de acesso ao app não pode ser formada só por espaços.", erro.Message);
        Assert.Empty(db.Funcionarios);
        Assert.Empty(db.Usuarios);
    }

    [Fact]
    public async Task Atualizar_ComVersaoDesatualizada_Lanca409()
    {
        var service = CriarServico(out var db);
        var f = await Criar(service, CriarDepartamentoECargo(db, "TI"), "Pessoa Teste");
        var edicao = Edicao(f, null);
        edicao.Telefone = "(11) 98765-4321";
        var atualizado = await service.AtualizarAsync(f.Id, edicao);
        Assert.Equal(f.Versao + 1, atualizado.Versao);

        edicao.Versao = f.Versao;
        await Assert.ThrowsAsync<ConflictException>(() => service.AtualizarAsync(f.Id, edicao));
    }

    // ── Folha, avaliação e agenda ───────────────────────────────────────

    [Fact]
    public async Task Folha_ComValoresOuCompetenciaForaDaFaixa_Lanca400()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var f = await Criar(new FuncionarioService(db), CriarDepartamentoECargo(db, "TI"), "Pessoa Teste");
        var folha = new FolhaPagamentoService(db);
        var competencia = TestUtils.Hoje.ToString("yyyy-MM");

        var liquidoNegativo = await Assert.ThrowsAsync<BusinessRuleException>(() => folha.CriarAsync(f.Id,
            new FolhaPagamentoCreateDto { Competencia = competencia, Salario = 1000m, Descontos = 50000m }));
        var salarioGigante = await Assert.ThrowsAsync<BusinessRuleException>(() => folha.CriarAsync(f.Id,
            new FolhaPagamentoCreateDto { Competencia = competencia, Salario = 1_000_000_000m }));
        var antesDaAdmissao = await Assert.ThrowsAsync<BusinessRuleException>(() => folha.CriarAsync(f.Id,
            new FolhaPagamentoCreateDto { Competencia = "2001-01", Salario = 1000m }));
        var muitoNoFuturo = await Assert.ThrowsAsync<BusinessRuleException>(() => folha.CriarAsync(f.Id,
            new FolhaPagamentoCreateDto { Competencia = "9999-12", Salario = 1000m }));
        var tresCasas = await Assert.ThrowsAsync<BusinessRuleException>(() => folha.CriarAsync(f.Id,
            new FolhaPagamentoCreateDto { Competencia = competencia, Salario = 0.005m }));

        Assert.Equal("Os descontos não podem ser maiores que o salário mais o bônus.", liquidoNegativo.Message);
        Assert.Equal("O salário da folha precisa estar entre R$ 0,00 e R$ 1.000.000,00.", salarioGigante.Message);
        Assert.StartsWith("A competência precisa estar entre 01/2024", antesDaAdmissao.Message);
        Assert.StartsWith("A competência precisa estar entre", muitoNoFuturo.Message);
        Assert.Equal("Informe o salário da folha com no máximo 2 casas decimais.", tresCasas.Message);
        Assert.Empty(db.FolhasPagamento);
    }

    [Fact]
    public async Task Avaliacao_ComNotaOuDataInvalida_Lanca400()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var f = await Criar(new FuncionarioService(db), CriarDepartamentoECargo(db, "TI"), "Pessoa Teste");
        var avaliacoes = new AvaliacaoService(db);

        var tresCasas = await Assert.ThrowsAsync<BusinessRuleException>(() => avaliacoes.CriarAsync(f.Id, new AvaliacaoCreateDto { Nota = 9.99m, DataAvaliacao = Ontem }));
        var acimaDe10 = await Assert.ThrowsAsync<BusinessRuleException>(() => avaliacoes.CriarAsync(f.Id, new AvaliacaoCreateDto { Nota = 10.5m, DataAvaliacao = Ontem }));
        var semData = await Assert.ThrowsAsync<BusinessRuleException>(() => avaliacoes.CriarAsync(f.Id, new AvaliacaoCreateDto { Nota = 9m }));
        var ano9999 = await Assert.ThrowsAsync<BusinessRuleException>(() => avaliacoes.CriarAsync(f.Id, new AvaliacaoCreateDto { Nota = 9m, DataAvaliacao = new DateTime(9999, 12, 31) }));
        var valida = await avaliacoes.CriarAsync(f.Id, new AvaliacaoCreateDto { Nota = 9.5m, DataAvaliacao = Ontem, Comentario = "  Muito   bom  " });

        Assert.Equal("A nota pode ter no máximo 1 casa decimal.", tresCasas.Message);
        Assert.Equal("A nota precisa estar entre 0 e 10.", acimaDe10.Message);
        Assert.Equal("Informe a data da avaliação.", semData.Message);
        Assert.StartsWith("A data da avaliação precisa estar entre", ano9999.Message);
        Assert.Equal("Muito bom", valida.Comentario);
    }

    [Fact]
    public async Task Agenda_ComTipoForaDaListaOuSemData_Lanca400()
    {
        var agenda = new EventoAgendaService(TestUtils.CriarDbContextEmMemoria());

        var tipo99 = await Assert.ThrowsAsync<BusinessRuleException>(() => agenda.CriarAsync(new EventoAgendaCreateDto { Titulo = "Reunião", DataEvento = DateTime.UtcNow, Tipo = (TipoEventoAgenda)99 }));
        var semData = await Assert.ThrowsAsync<BusinessRuleException>(() => agenda.CriarAsync(new EventoAgendaCreateDto { Titulo = "Reunião" }));
        var longe = await Assert.ThrowsAsync<BusinessRuleException>(() => agenda.CriarAsync(new EventoAgendaCreateDto { Titulo = "Reunião", DataEvento = new DateTime(9999, 1, 1) }));
        var semTipo = await agenda.CriarAsync(new EventoAgendaCreateDto { Titulo = "Reunião", DataEvento = DateTime.UtcNow });

        Assert.Equal("Tipo de evento inválido.", tipo99.Message);
        Assert.Equal("Informe a data/hora do evento.", semData.Message);
        Assert.StartsWith("A data do evento precisa estar entre", longe.Message);
        Assert.Equal(TipoEventoAgenda.Outro, semTipo.Tipo);
    }

    // ── Perfil das contas ───────────────────────────────────────────────

    [Fact]
    public async Task AlterarPerfil_ProprioPerfil_Lanca400()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var admin = TestUtils.CriarUsuario(db, "admin@empresa.com", perfil: PerfilUsuario.Admin);

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new UsuarioService(db).AlterarPerfilAsync(admin.Id, admin.Id, new AlterarPerfilUsuarioDto { Perfil = PerfilUsuario.Rh }));

        Assert.Equal("Você não pode alterar o seu próprio perfil.", erro.Message);
    }

    [Fact]
    public async Task AlterarPerfil_SolicitanteQueJaFoiRebaixado_Lanca403()
    {
        // Situação de corrida: o token ainda diz Admin, mas outro Admin já
        // rebaixou quem pede. O perfil é relido do banco dentro da trava.
        var db = TestUtils.CriarDbContextEmMemoria();
        var solicitante = TestUtils.CriarUsuario(db, "ex-admin@empresa.com", perfil: PerfilUsuario.Rh);
        var ultimoAdmin = TestUtils.CriarUsuario(db, "admin@empresa.com", perfil: PerfilUsuario.Admin);

        var erro = await Assert.ThrowsAsync<ForbiddenException>(() =>
            new UsuarioService(db).AlterarPerfilAsync(solicitante.Id, ultimoAdmin.Id, new AlterarPerfilUsuarioDto { Perfil = PerfilUsuario.Funcionario }));

        Assert.Equal("Somente um administrador pode alterar perfis.", erro.Message);
        Assert.Equal(PerfilUsuario.Admin, db.Usuarios.Single(u => u.Id == ultimoAdmin.Id).Perfil);
    }

    [Fact]
    public async Task AlterarPerfil_AdminLigadoAColaboradorDesligado_NaoContaComoAdmin()
    {
        // O Admin "A" ainda tem perfil Admin, mas o colaborador dele foi
        // desligado: não entra mais, então não pode agir nem contar como Admin.
        var db = TestUtils.CriarDbContextEmMemoria();
        var service = new FuncionarioService(db);
        var lugar = CriarDepartamentoECargo(db, "TI");
        var colaboradorA = await Criar(service, lugar, "Admin Desligado", email: "a@empresa.com");
        await service.DefinirAcessoAppAsync(colaboradorA.Id, "senha123");
        var contaA = db.Usuarios.Single(u => u.FuncionarioId == colaboradorA.Id);
        contaA.Perfil = PerfilUsuario.Admin;
        db.Funcionarios.Single(f => f.Id == colaboradorA.Id).Status = StatusFuncionario.Desligado;
        await db.SaveChangesAsync();
        var adminB = TestUtils.CriarUsuario(db, "b@empresa.com", perfil: PerfilUsuario.Admin);
        var rh = TestUtils.CriarUsuario(db, "rh@empresa.com", perfil: PerfilUsuario.Rh);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new UsuarioService(db).AlterarPerfilAsync(contaA.Id, rh.Id, new AlterarPerfilUsuarioDto { Perfil = PerfilUsuario.Admin }));
        Assert.Equal(0, await UsuarioService.ContarAdminsAtivosAsync(db, excetoUsuarioId: adminB.Id));
    }

    [Fact]
    public async Task AlterarPerfil_ComPerfilAtualDesatualizado_Lanca409()
    {
        // Outro Admin mudou o perfil depois que a tela carregou.
        var db = TestUtils.CriarDbContextEmMemoria();
        var admin = TestUtils.CriarUsuario(db, "admin@empresa.com", perfil: PerfilUsuario.Admin);
        var alvo = TestUtils.CriarUsuario(db, "alvo@empresa.com", perfil: PerfilUsuario.Admin);

        var erro = await Assert.ThrowsAsync<ConflictException>(() => new UsuarioService(db).AlterarPerfilAsync(admin.Id, alvo.Id,
            new AlterarPerfilUsuarioDto { Perfil = PerfilUsuario.Rh, PerfilAtual = PerfilUsuario.Funcionario }));

        Assert.Equal("Este registro foi alterado por outra pessoa. Atualize a tela e tente de novo.", erro.Message);
        Assert.Equal(PerfilUsuario.Admin, db.Usuarios.Single(u => u.Id == alvo.Id).Perfil);

        var certo = await new UsuarioService(db).AlterarPerfilAsync(admin.Id, alvo.Id,
            new AlterarPerfilUsuarioDto { Perfil = PerfilUsuario.Rh, PerfilAtual = PerfilUsuario.Admin });
        Assert.Equal(PerfilUsuario.Rh, certo.Perfil);
    }

    // ── Contas RH/Admin ligadas a colaborador (RT6-01) ──────────────────

    private static async Task<(FuncionarioService Service, AppDbContext Db, FuncionarioDto Colaborador, Usuario Conta, Usuario Rh, Usuario Admin)>
        CenarioContaPrivilegiada(PerfilUsuario perfilDaConta)
    {
        var service = CriarServico(out var db);
        var colaborador = await Criar(service, CriarDepartamentoECargo(db, "TI"), "Pessoa Privilegiada", email: "conta@empresa.com");
        await service.DefinirAcessoAppAsync(colaborador.Id, "senha123");
        var conta = db.Usuarios.Single(u => u.FuncionarioId == colaborador.Id);
        conta.Perfil = perfilDaConta;
        await db.SaveChangesAsync();
        var rh = TestUtils.CriarUsuario(db, "rh@empresa.com", perfil: PerfilUsuario.Rh);
        var admin = TestUtils.CriarUsuario(db, "admin@empresa.com", perfil: PerfilUsuario.Admin);
        return (service, db, colaborador, conta, rh, admin);
    }

    [Theory]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.Rh)]
    public async Task ContaPrivilegiada_RhNaoRedefineSenhaNemTrocaEmailNemDesliga(PerfilUsuario perfilDaConta)
    {
        var (service, db, colaborador, conta, rh, _) = await CenarioContaPrivilegiada(perfilDaConta);
        var hashAntes = conta.SenhaHash;

        var senha = await Assert.ThrowsAsync<ForbiddenException>(() => service.DefinirAcessoAppAsync(colaborador.Id, "senhaDoRh1", rh.Id));
        var email = await Assert.ThrowsAsync<ForbiddenException>(() => service.AtualizarAsync(colaborador.Id,
            new FuncionarioUpdateDto { Nome = colaborador.Nome, Email = "rh-controla@empresa.com", DepartamentoId = colaborador.DepartamentoId, CargoId = colaborador.CargoId },
            rh.Id));
        var desligar = await Assert.ThrowsAsync<ForbiddenException>(() => service.DesligarAsync(colaborador.Id, null, rh.Id));

        Assert.Equal("Somente um administrador pode alterar o acesso de contas RH/Admin.", senha.Message);
        Assert.Equal("Somente um administrador pode alterar o acesso de contas RH/Admin.", email.Message);
        Assert.Equal("Somente um administrador pode desligar um colaborador com conta RH/Admin.", desligar.Message);
        var contaDepois = db.Usuarios.AsNoTracking().Single(u => u.Id == conta.Id);
        Assert.Equal(hashAntes, contaDepois.SenhaHash);
        Assert.Equal("conta@empresa.com", contaDepois.Email);
        Assert.Equal(StatusFuncionario.Ativo, db.Funcionarios.AsNoTracking().Single(f => f.Id == colaborador.Id).Status);
    }

    [Fact]
    public async Task ContaPrivilegiada_RhAindaEditaOutrosDadosSemMexerNoEmail()
    {
        var (service, _, colaborador, _, rh, _) = await CenarioContaPrivilegiada(PerfilUsuario.Admin);

        var atualizado = await service.AtualizarAsync(colaborador.Id,
            new FuncionarioUpdateDto { Nome = colaborador.Nome, Email = colaborador.Email, Telefone = "11987654321", DepartamentoId = colaborador.DepartamentoId, CargoId = colaborador.CargoId },
            rh.Id);

        Assert.Equal("(11) 98765-4321", atualizado.Telefone);
    }

    [Fact]
    public async Task ContaPrivilegiada_AdminRedefineSenhaETrocaEmail()
    {
        var (service, db, colaborador, conta, _, admin) = await CenarioContaPrivilegiada(PerfilUsuario.Rh);

        await service.DefinirAcessoAppAsync(colaborador.Id, "novaSenha1", admin.Id);
        await service.AtualizarAsync(colaborador.Id,
            new FuncionarioUpdateDto { Nome = colaborador.Nome, Email = "novo@empresa.com", DepartamentoId = colaborador.DepartamentoId, CargoId = colaborador.CargoId },
            admin.Id);

        Assert.Equal("novo@empresa.com", db.Usuarios.Single(u => u.Id == conta.Id).Email);
    }

    [Fact]
    public async Task ContaComum_RhContinuaRedefinindoSenhaETrocandoEmail()
    {
        var (service, db, colaborador, conta, rh, _) = await CenarioContaPrivilegiada(PerfilUsuario.Funcionario);

        await service.DefinirAcessoAppAsync(colaborador.Id, "novaSenha1", rh.Id);
        await service.AtualizarAsync(colaborador.Id,
            new FuncionarioUpdateDto { Nome = colaborador.Nome, Email = "novo@empresa.com", DepartamentoId = colaborador.DepartamentoId, CargoId = colaborador.CargoId },
            rh.Id);
        await service.DesligarAsync(colaborador.Id, null, rh.Id);

        Assert.Equal("novo@empresa.com", db.Usuarios.Single(u => u.Id == conta.Id).Email);
    }

    [Fact]
    public async Task Desligar_OUltimoAdminAtivo_Lanca400()
    {
        // O único Admin que consegue entrar é a conta ligada a esse colaborador
        // (o outro Admin está ligado a um colaborador já desligado).
        var (service, db, colaborador, _, _, admin) = await CenarioContaPrivilegiada(PerfilUsuario.Admin);
        var outro = await Criar(service, CriarDepartamentoECargo(db, "Vendas"), "Admin Que Saiu", email: "saiu@empresa.com");
        await service.DefinirAcessoAppAsync(outro.Id, "senha123");
        db.Usuarios.Single(u => u.FuncionarioId == outro.Id).Perfil = PerfilUsuario.Admin;
        db.Funcionarios.Single(f => f.Id == outro.Id).Status = StatusFuncionario.Desligado;
        db.Usuarios.Single(u => u.Id == admin.Id).Perfil = PerfilUsuario.Rh;
        await db.SaveChangesAsync();
        var contaDoColaborador = db.Usuarios.Single(u => u.FuncionarioId == colaborador.Id);

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DesligarAsync(colaborador.Id, null, contaDoColaborador.Id));

        Assert.Equal("Não é possível desligar o último administrador ativo do sistema.", erro.Message);
    }

    [Fact]
    public async Task Desligar_AdminComOutroAdminAtivo_EhPermitidoParaAdmin()
    {
        var (service, db, colaborador, conta, _, admin) = await CenarioContaPrivilegiada(PerfilUsuario.Admin);

        var resultado = await service.DesligarAsync(colaborador.Id, null, admin.Id);

        Assert.Equal(StatusFuncionario.Desligado, resultado.Funcionario.Status);
        Assert.Equal(1, await UsuarioService.ContarAdminsAtivosAsync(db, excetoUsuarioId: conta.Id));
    }

    [Fact]
    public async Task AlterarPerfil_DeOutraConta_GravaESobeAVersaoDoToken()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var admin = TestUtils.CriarUsuario(db, "admin@empresa.com", perfil: PerfilUsuario.Admin);
        var rh = TestUtils.CriarUsuario(db, "rh@empresa.com", perfil: PerfilUsuario.Rh);

        var resultado = await new UsuarioService(db).AlterarPerfilAsync(admin.Id, rh.Id,
            new AlterarPerfilUsuarioDto { Perfil = PerfilUsuario.Admin, SenhaProvisoria = "provisoria1" });

        Assert.Equal(PerfilUsuario.Admin, resultado.Perfil);
        Assert.Equal(1, db.Usuarios.Single(u => u.Id == rh.Id).TokenVersao);
    }
}
