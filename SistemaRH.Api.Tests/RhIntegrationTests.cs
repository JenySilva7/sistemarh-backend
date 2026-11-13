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

    private async Task<string> Logar(string email, string senha)
    {
        var resposta = await CriarCliente().PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Senha = senha });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await LerJson(resposta)).GetProperty("token").GetString()!;
    }

    private async Task<string> CriarContaRhELogar()
    {
        var email = $"rh-{Guid.NewGuid()}@empresa.com";
        await CriarCliente().PostAsJsonAsync("/api/auth/register", new RegisterDto { Nome = "Equipe RH", Email = email, Senha = "senha123" });
        await UsarBanco(db =>
        {
            db.Usuarios.Single(u => u.Email == email).Perfil = PerfilUsuario.Rh;
            return Task.CompletedTask;
        });
        return await Logar(email, "senha123");
    }

    private async Task<(int DepartamentoId, int CargoId)> CriarDepartamentoECargo()
    {
        var departamento = new Departamento { Nome = $"Depto {Guid.NewGuid()}" };
        var cargo = new Cargo { Nome = $"Cargo {Guid.NewGuid()}" };
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
            Cpf = Guid.NewGuid().ToString(),
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
        var envio = await app.PostAsJsonAsync("/api/ausencias", new
        {
            tipo = 0,
            data = "2026-11-10T00:00:00",
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
        Assert.Equal("2026-11-10T00:00:00", ausencia.GetProperty("data").GetString());
        Assert.EndsWith("Z", ausencia.GetProperty("criadoEm").GetString());

        var aprovacao = await rh.PutAsJsonAsync($"/api/rh/ausencias/{ausenciaId}/status", new { status = 2 });
        Assert.Equal(HttpStatusCode.OK, aprovacao.StatusCode);
        Assert.Equal(2, (await LerJson(aprovacao)).GetProperty("status").GetInt32());

        var mudarDeNovo = await rh.PutAsJsonAsync($"/api/rh/ausencias/{ausenciaId}/status", new { status = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, mudarDeNovo.StatusCode);
        Assert.Equal("Essa justificativa já foi concluída e não pode mais ser alterada.", await LerMensagem(mudarDeNovo));

        // Desligado: perde o acesso, inclusive com o token antigo.
        var desligamento = await rh.PostAsJsonAsync($"/api/funcionarios/{funcionarioId}/desligar", new { });
        Assert.Equal(HttpStatusCode.OK, desligamento.StatusCode);

        var me = await app.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal("Seu acesso foi desativado. Procure o RH.", await LerMensagem(me));

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
        var email = $"colab-{Guid.NewGuid()}@empresa.com";
        await CriarCliente().PostAsJsonAsync("/api/auth/register", new RegisterDto { Nome = "Colaborador", Email = email, Senha = "senha123" });
        var app = CriarCliente(await Logar(email, "senha123"));

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
}
