using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Models;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Fluxo completo entre o painel do RH e o app: o RH cadastra o colaborador
/// já com acesso, o colaborador entra no app, bate ponto e envia uma
/// justificativa, e o RH enxerga/aprova tudo. Confere também o formato do
/// JSON (nomes dos campos e o "Z" só nos instantes).
/// </summary>
public class RhIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    // A vigência vai da admissão até hoje (data futura é recusada).
    private static readonly string Vigencia = TestUtils.Iso(TestUtils.Hoje);

    public RhIntegrationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CriarCliente(string? token = null)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private async Task UsarBanco(Func<AppDbContext, Task> acao)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await acao(db);
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> LerMensagem(HttpResponseMessage resposta) =>
        (await LerJson(resposta)).GetProperty("mensagem").GetString();

    // O banco é compartilhado entre os testes da classe, então cada cadastro
    // precisa de um CPF válido e diferente.
    private static string GerarCpfValido()
    {
        var numeros = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)).ToList();
        for (var quantidade = 9; quantidade <= 10; quantidade++)
        {
            var soma = numeros.Select((n, i) => n * (quantidade + 1 - i)).Sum();
            var resto = soma * 10 % 11;
            numeros.Add(resto == 10 ? 0 : resto);
        }
        var d = string.Concat(numeros);
        return $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}";
    }

    private async Task<string> Logar(string email, string senha)
    {
        var resposta = await CriarCliente().PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Senha = senha });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await LerJson(resposta)).GetProperty("token").GetString()!;
    }

    // O cadastro público não existe mais: as contas de teste nascem direto no banco.
    private async Task<string> CriarContaELogar(PerfilUsuario perfil, string nome)
    {
        var email = $"{perfil.ToString().ToLowerInvariant()}-{Guid.NewGuid()}@empresa.com";
        await UsarBanco(db =>
        {
            TestUtils.CriarUsuario(db, email, perfil: perfil, nome: nome);
            return Task.CompletedTask;
        });
        return await Logar(email, "senha123");
    }

    private Task<string> CriarContaRhELogar() => CriarContaELogar(PerfilUsuario.Rh, "Equipe RH");

    private async Task<(int DepartamentoId, int CargoId)> CriarDepartamentoECargo()
    {
        var departamento = new Departamento { Nome = $"Depto {Guid.NewGuid()}" };
        var cargo = new Cargo { Nome = $"Cargo {Guid.NewGuid()}", SalarioBase = 4000m };
        await UsarBanco(db =>
        {
            db.Departamentos.Add(departamento);
            db.Cargos.Add(cargo);
            return Task.CompletedTask;
        });
        return (departamento.Id, cargo.Id);
    }

    private async Task<HttpResponseMessage> CriarFuncionario(HttpClient rh, string email, string? senhaAcessoApp)
    {
        var (departamentoId, cargoId) = await CriarDepartamentoECargo();
        return await rh.PostAsJsonAsync("/api/funcionarios", new FuncionarioCreateDto
        {
            Nome = "Carla Dias",
            Cpf = GerarCpfValido(),
            Email = email,
            DataAdmissao = new DateTime(2025, 3, 10),
            DepartamentoId = departamentoId,
            CargoId = cargoId,
            SenhaAcessoApp = senhaAcessoApp,
        });
    }

    [Fact]
    public async Task FluxoCompleto_CadastroComAcesso_PontoEAusenciaChegamAoRh()
    {
        var rh = CriarCliente(await CriarContaRhELogar());
        var email = $"carla-{Guid.NewGuid()}@empresa.com";

        // RH cadastra já com acesso ao app.
        var cadastro = await CriarFuncionario(rh, email, "senha123");
        Assert.Equal(HttpStatusCode.Created, cadastro.StatusCode);
        var funcionarioJson = await LerJson(cadastro);
        Assert.True(funcionarioJson.GetProperty("possuiAcessoApp").GetBoolean());
        var funcionarioId = funcionarioJson.GetProperty("id").GetInt32();

        // Colaborador entra no app com a senha definida pelo RH.
        var login = await CriarCliente().PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Senha = "senha123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginJson = await LerJson(login);
        var usuarioJson = loginJson.GetProperty("usuario");
        Assert.Equal(funcionarioId, usuarioJson.GetProperty("funcionarioId").GetInt32());
        Assert.Equal("2025-03-10T00:00:00", usuarioJson.GetProperty("dataAdmissao").GetString()); // data de calendário: sem "Z"
        Assert.Equal(JsonValueKind.Null, usuarioJson.GetProperty("gerenteNome").ValueKind);
        var usuarioId = usuarioJson.GetProperty("id").GetInt32();
        var app = CriarCliente(loginJson.GetProperty("token").GetString());

        // Simula o MySQL: o horário volta do banco sem fuso (Unspecified).
        await UsarBanco(db =>
        {
            db.RegistrosPonto.Add(new RegistroPonto
            {
                UsuarioId = usuarioId,
                Tipo = TipoRegistroPonto.Entrada,
                RegistradoEm = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            });
            return Task.CompletedTask;
        });

        var pontoHoje = await LerJson(await app.GetAsync("/api/ponto/hoje"));
        Assert.EndsWith("Z", pontoHoje[0].GetProperty("registradoEm").GetString());

        var pontoRh = await rh.GetAsync("/api/rh/ponto");
        Assert.Equal(HttpStatusCode.OK, pontoRh.StatusCode);
        var marcacao = (await LerJson(pontoRh)).EnumerateArray()
            .Single(r => r.GetProperty("usuarioEmail").GetString() == email);
        Assert.EndsWith("Z", marcacao.GetProperty("registradoEm").GetString());
        Assert.Equal(usuarioId, marcacao.GetProperty("usuarioId").GetInt32());
        Assert.Equal("Carla Dias", marcacao.GetProperty("usuarioNome").GetString());
        Assert.Equal(0, marcacao.GetProperty("tipo").GetInt32());

        // Colaborador envia uma justificativa; o RH vê e aprova.
        var dataAusencia = TestUtils.Hoje.AddDays(-8);
        var envio = await app.PostAsJsonAsync("/api/ausencias", new
        {
            tipo = 0,
            data = TestUtils.IsoJson(dataAusencia),
            diaInteiro = true,
            descricao = "Consulta médica",
        });
        Assert.Equal(HttpStatusCode.Created, envio.StatusCode);
        var ausenciaId = (await LerJson(envio)).GetProperty("id").GetInt32();

        var ausenciasRh = await LerJson(await rh.GetAsync("/api/rh/ausencias?status=0"));
        var ausencia = ausenciasRh.EnumerateArray().Single(a => a.GetProperty("id").GetInt32() == ausenciaId);
        Assert.Equal(funcionarioId, ausencia.GetProperty("funcionarioId").GetInt32());
        Assert.Equal(usuarioId, ausencia.GetProperty("usuarioId").GetInt32());
        Assert.Equal(email, ausencia.GetProperty("usuarioEmail").GetString());
        Assert.Equal(TestUtils.IsoJson(dataAusencia), ausencia.GetProperty("data").GetString());
        Assert.EndsWith("Z", ausencia.GetProperty("criadoEm").GetString());
        var versao = ausencia.GetProperty("versao").GetInt32();

        var aprovacao = await rh.PutAsJsonAsync($"/api/rh/ausencias/{ausenciaId}/status", new { status = 2, versao });
        Assert.Equal(HttpStatusCode.OK, aprovacao.StatusCode);
        Assert.Equal(2, (await LerJson(aprovacao)).GetProperty("status").GetInt32());

        var mudarDeNovo = await rh.PutAsJsonAsync($"/api/rh/ausencias/{ausenciaId}/status", new { status = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, mudarDeNovo.StatusCode);
        Assert.Equal("Essa justificativa já foi concluída e não pode mais ser alterada.", await LerMensagem(mudarDeNovo));

        // Desligado: perde o acesso NA HORA, inclusive com o token antigo.
        var desligamento = await rh.PostAsJsonAsync($"/api/funcionarios/{funcionarioId}/desligar", new { });
        Assert.Equal(HttpStatusCode.OK, desligamento.StatusCode);
        Assert.Equal(1, (await LerJson(desligamento)).GetProperty("funcionario").GetProperty("status").GetInt32());

        var me = await app.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal("Seu acesso foi desativado. Procure o RH.", await LerMensagem(me));
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.PostAsync("/api/ponto", null)).StatusCode);

        var novoLogin = await CriarCliente().PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Senha = "senha123" });
        Assert.Equal(HttpStatusCode.BadRequest, novoLogin.StatusCode);
        Assert.Equal("Seu acesso foi desativado. Procure o RH.", await LerMensagem(novoLogin));
    }

    [Fact]
    public async Task AcessoApp_DefinidoDepoisDoCadastro_ValidaSenhaELiberaLogin()
    {
        var rh = CriarCliente(await CriarContaRhELogar());
        var email = $"carla-{Guid.NewGuid()}@empresa.com";
        var cadastro = await CriarFuncionario(rh, email, senhaAcessoApp: null);
        var funcionarioJson = await LerJson(cadastro);
        Assert.False(funcionarioJson.GetProperty("possuiAcessoApp").GetBoolean());
        var funcionarioId = funcionarioJson.GetProperty("id").GetInt32();

        var senhaCurta = await rh.PostAsJsonAsync($"/api/funcionarios/{funcionarioId}/acesso-app", new { senha = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, senhaCurta.StatusCode);
        Assert.Equal("A senha de acesso ao app precisa ter pelo menos 6 caracteres.", await LerMensagem(senhaCurta));

        var senhaNula = await rh.PostAsJsonAsync($"/api/funcionarios/{funcionarioId}/acesso-app", new { senha = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, senhaNula.StatusCode);
        Assert.Equal("A senha de acesso ao app precisa ter pelo menos 6 caracteres.", await LerMensagem(senhaNula));

        var resposta = await rh.PostAsJsonAsync($"/api/funcionarios/{funcionarioId}/acesso-app", new { senha = "senha123" });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True((await LerJson(resposta)).GetProperty("possuiAcessoApp").GetBoolean());

        await Logar(email, "senha123");
    }

    [Fact]
    public async Task RotasDoRh_ComContaDeColaborador_Devolvem403()
    {
        var app = CriarCliente(await CriarContaELogar(PerfilUsuario.Funcionario, "Colaborador"));

        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetAsync("/api/rh/ausencias")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetAsync("/api/rh/ponto")).StatusCode);
    }

    [Fact]
    public async Task PontoDoRh_ComDataInvalida_Devolve400()
    {
        var rh = CriarCliente(await CriarContaRhELogar());

        var resposta = await rh.GetAsync("/api/rh/ponto?data=13-11-2026");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("Data inválida. Use o formato AAAA-MM-DD.", await LerMensagem(resposta));
    }

    // ── Remuneração (salário, vale alimentação e promoção) ──────────────

    [Fact]
    public async Task Remuneracao_ComContaDeColaborador_Devolve403()
    {
        var app = CriarCliente(await CriarContaELogar(PerfilUsuario.Funcionario, "Colaborador"));
        var corpo = new { novoSalario = 5500.00, novoValor = 700.00, novoCargoId = 1, novoDepartamentoId = 1, dataVigencia = Vigencia, motivo = "Reajuste anual" };

        Assert.Equal(HttpStatusCode.Forbidden, (await app.PutAsJsonAsync("/api/funcionarios/1/salario", corpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.PutAsJsonAsync("/api/funcionarios/1/vale-alimentacao", corpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.PostAsJsonAsync("/api/funcionarios/1/promocao", corpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetAsync("/api/funcionarios/1/ajustes-remuneracao")).StatusCode);
    }

    [Fact]
    public async Task Remuneracao_ContaDoRh_AjustaPromoveEConsultaOHistorico()
    {
        var rh = CriarCliente(await CriarContaRhELogar());
        var departamento = new Departamento { Nome = $"Depto {Guid.NewGuid()}" };
        var novoDepartamento = new Departamento { Nome = $"Depto {Guid.NewGuid()}" };
        var cargo = new Cargo { Nome = $"Cargo {Guid.NewGuid()}", SalarioBase = 4000m };
        var novoCargo = new Cargo { Nome = $"Cargo {Guid.NewGuid()}", SalarioBase = 8000m };
        await UsarBanco(db =>
        {
            db.AddRange(departamento, novoDepartamento, cargo, novoCargo);
            return Task.CompletedTask;
        });

        // Cadastro sem salário nem VA: vêm do cargo e do valor padrão.
        var cadastro = await rh.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "Diego Ramos",
            cpf = GerarCpfValido(),
            dataAdmissao = "2025-03-10",
            departamentoId = departamento.Id,
            cargoId = cargo.Id,
        });
        Assert.Equal(HttpStatusCode.Created, cadastro.StatusCode);
        var cadastroJson = await LerJson(cadastro);
        Assert.Equal(4000m, cadastroJson.GetProperty("salario").GetDecimal());
        Assert.Equal(600m, cadastroJson.GetProperty("valeAlimentacao").GetDecimal());
        var id = cadastroJson.GetProperty("id").GetInt32();

        var salario = await rh.PutAsJsonAsync($"/api/funcionarios/{id}/salario", new { novoSalario = 4500.00, dataVigencia = Vigencia, motivo = "Reajuste anual" });
        Assert.Equal(HttpStatusCode.OK, salario.StatusCode);
        Assert.Equal(4500m, (await LerJson(salario)).GetProperty("salario").GetDecimal());

        var vale = await rh.PutAsJsonAsync($"/api/funcionarios/{id}/vale-alimentacao", new { novoValor = 700.00, dataVigencia = Vigencia, motivo = "Novo acordo coletivo" });
        Assert.Equal(HttpStatusCode.OK, vale.StatusCode);
        Assert.Equal(700m, (await LerJson(vale)).GetProperty("valeAlimentacao").GetDecimal());

        var promocao = await rh.PostAsJsonAsync($"/api/funcionarios/{id}/promocao", new
        {
            novoCargoId = novoCargo.Id,
            novoDepartamentoId = novoDepartamento.Id,
            novoSalario = 9000.00,
            dataVigencia = Vigencia,
            motivo = "Promoção a coordenador",
        });
        Assert.Equal(HttpStatusCode.OK, promocao.StatusCode);
        var promocaoJson = await LerJson(promocao);
        Assert.Equal(0, promocaoJson.GetProperty("subordinadosRealocados").GetInt32());
        var promovido = promocaoJson.GetProperty("funcionario");
        Assert.Equal(novoCargo.Id, promovido.GetProperty("cargoId").GetInt32());
        Assert.Equal(novoCargo.Nome, promovido.GetProperty("cargoNome").GetString());
        Assert.Equal(novoDepartamento.Id, promovido.GetProperty("departamentoId").GetInt32());
        Assert.Equal(novoDepartamento.Nome, promovido.GetProperty("departamentoNome").GetString());
        Assert.Equal(9000m, promovido.GetProperty("salario").GetDecimal());
        Assert.Equal(700m, promovido.GetProperty("valeAlimentacao").GetDecimal());

        var historicoResposta = await rh.GetAsync($"/api/funcionarios/{id}/ajustes-remuneracao");
        Assert.Equal(HttpStatusCode.OK, historicoResposta.StatusCode);
        var historico = (await LerJson(historicoResposta)).EnumerateArray().ToList();
        Assert.Equal(new[] { 2, 1, 0 }, historico.Select(a => a.GetProperty("tipo").GetInt32()));

        var ultimo = historico[0];
        Assert.Equal(4500m, ultimo.GetProperty("valorAnterior").GetDecimal());
        Assert.Equal(9000m, ultimo.GetProperty("valorNovo").GetDecimal());
        Assert.Equal(cargo.Nome, ultimo.GetProperty("cargoAnteriorNome").GetString());
        Assert.Equal(novoCargo.Nome, ultimo.GetProperty("cargoNovoNome").GetString());
        Assert.Equal(departamento.Nome, ultimo.GetProperty("departamentoAnteriorNome").GetString());
        Assert.Equal(novoDepartamento.Nome, ultimo.GetProperty("departamentoNovoNome").GetString());
        Assert.Equal(TestUtils.IsoJson(TestUtils.Hoje), ultimo.GetProperty("dataVigencia").GetString()); // data de calendário: sem "Z"
        Assert.Equal("Promoção a coordenador", ultimo.GetProperty("motivo").GetString());
        Assert.Equal("Equipe RH", ultimo.GetProperty("responsavelNome").GetString());
        Assert.EndsWith("Z", ultimo.GetProperty("criadoEm").GetString());
        Assert.True(ultimo.GetProperty("id").GetInt32() > 0);

        var primeiro = historico[2];
        Assert.Equal(4000m, primeiro.GetProperty("valorAnterior").GetDecimal());
        Assert.Equal(4500m, primeiro.GetProperty("valorNovo").GetDecimal());
        Assert.Equal(JsonValueKind.Null, primeiro.GetProperty("cargoAnteriorNome").ValueKind);
        Assert.Equal(JsonValueKind.Null, primeiro.GetProperty("departamentoNovoNome").ValueKind);
    }

    [Fact]
    public async Task Remuneracao_ComDadosInvalidosOuFuncionarioInexistente_DevolveMensagem()
    {
        var rh = CriarCliente(await CriarContaRhELogar());
        var cadastro = await CriarFuncionario(rh, $"eva-{Guid.NewGuid()}@empresa.com", senhaAcessoApp: null);
        var id = (await LerJson(cadastro)).GetProperty("id").GetInt32();

        var salarioZero = await rh.PutAsJsonAsync($"/api/funcionarios/{id}/salario", new { novoSalario = 0, dataVigencia = Vigencia, motivo = "Reajuste anual" });
        Assert.Equal(HttpStatusCode.BadRequest, salarioZero.StatusCode);
        Assert.Equal("O salário precisa estar entre R$ 1,00 e R$ 1.000.000,00.", await LerMensagem(salarioZero));

        var semMotivo = await rh.PutAsJsonAsync($"/api/funcionarios/{id}/salario", new { novoSalario = 5000, dataVigencia = Vigencia, motivo = "" });
        Assert.Equal(HttpStatusCode.BadRequest, semMotivo.StatusCode);
        Assert.Contains("Informe o motivo do ajuste.", await LerMensagem(semMotivo));

        var valeNegativo = await rh.PutAsJsonAsync($"/api/funcionarios/{id}/vale-alimentacao", new { novoValor = -1, dataVigencia = Vigencia, motivo = "Reajuste anual" });
        Assert.Equal(HttpStatusCode.BadRequest, valeNegativo.StatusCode);
        Assert.Equal("O vale alimentação precisa estar entre R$ 0,00 e R$ 10.000,00.", await LerMensagem(valeNegativo));

        var semData = await rh.PutAsJsonAsync($"/api/funcionarios/{id}/salario", new { novoSalario = 5000, motivo = "Reajuste anual" });
        Assert.Equal(HttpStatusCode.BadRequest, semData.StatusCode);
        Assert.Equal("Informe a data de vigência.", await LerMensagem(semData));

        var inexistente = await rh.PutAsJsonAsync("/api/funcionarios/999999/salario", new { novoSalario = 5000, dataVigencia = Vigencia, motivo = "Reajuste anual" });
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("Funcionário não encontrado.", await LerMensagem(inexistente));

        var historicoInexistente = await rh.GetAsync("/api/funcionarios/999999/ajustes-remuneracao");
        Assert.Equal(HttpStatusCode.NotFound, historicoInexistente.StatusCode);
        Assert.Equal("Funcionário não encontrado.", await LerMensagem(historicoInexistente));
    }

    [Fact]
    public async Task Cadastro_ComCpfJaExistenteSemMascara_Retorna409()
    {
        var rh = CriarCliente(await CriarContaRhELogar());
        var (departamentoId, cargoId) = await CriarDepartamentoECargo();
        var cpf = GerarCpfValido();
        object Corpo(string cpfEnviado, string email) => new
        {
            nome = "Helena Prado",
            cpf = cpfEnviado,
            email,
            dataAdmissao = "2025-03-10",
            departamentoId,
            cargoId,
        };

        var primeiro = await rh.PostAsJsonAsync("/api/funcionarios", Corpo(cpf, $"helena-{Guid.NewGuid()}@empresa.com"));
        Assert.Equal(HttpStatusCode.Created, primeiro.StatusCode);

        var semMascara = cpf.Replace(".", "").Replace("-", "");
        var duplicado = await rh.PostAsJsonAsync("/api/funcionarios", Corpo(semMascara, $"helena-{Guid.NewGuid()}@empresa.com"));
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
        Assert.Equal("Já existe um funcionário cadastrado com esse CPF.", await LerMensagem(duplicado));
    }

    [Theory]
    [InlineData("123.456.789-00")]
    [InlineData("111.111.111-11")]
    [InlineData("1234")]
    public async Task Cadastro_ComCpfInvalido_Retorna400(string cpf)
    {
        var rh = CriarCliente(await CriarContaRhELogar());
        var (departamentoId, cargoId) = await CriarDepartamentoECargo();

        var resposta = await rh.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "Otávio Reis",
            cpf,
            dataAdmissao = "2025-03-10",
            departamentoId,
            cargoId,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("CPF inválido. Confira os números digitados.", await LerMensagem(resposta));
    }

    [Fact]
    public async Task AjusteSalario_ComDataEmFormatoInvalido_RetornaMensagemEmPortugues()
    {
        var rh = CriarCliente(await CriarContaRhELogar());
        var cadastro = await CriarFuncionario(rh, $"lia-{Guid.NewGuid()}@empresa.com", null);
        var id = (await LerJson(cadastro)).GetProperty("id").GetInt32();

        var resposta = await rh.PutAsJsonAsync($"/api/funcionarios/{id}/salario", new { novoSalario = 8000, dataVigencia = "31/02/2026", motivo = "Reajuste" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("Data de vigência está em formato inválido.", await LerMensagem(resposta));
    }
}
