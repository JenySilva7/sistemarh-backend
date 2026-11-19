using System.Net;
using System.Net.Http.Json;
using SistemaRH.Api.Models;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Rotas novas do colaborador: /api/me (cadastro, holerites e histórico de
/// remuneração da própria pessoa) e o "Falar com o RH" (/api/mensagens-rh e
/// /api/rh/mensagens). Também cobre ponto e justificativa duplicados pela API.
/// </summary>
public class MensagensEMeIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly ApiDeTeste _api;

    public MensagensEMeIntegrationTests(IntegrationTestFactory factory)
    {
        _api = new ApiDeTeste(factory);
    }

    // ── /api/me ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MeuCadastro_ContaLigada_DevolveOProprioCadastroHoleritesEAjustes()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var rh = _api.Cliente(tokenRh);
        var (funcionario, _, tokenApp) = await _api.CriarColaboradorComAcesso(rh, "Marta Moura");
        var id = funcionario.GetProperty("id").GetInt32();
        var competenciaAtual = TestUtils.Hoje.ToString("yyyy-MM");
        var competenciaAnterior = TestUtils.Hoje.AddMonths(-1).ToString("yyyy-MM");
        Assert.Equal(HttpStatusCode.Created, (await rh.PostAsJsonAsync($"/api/folha-pagamento?funcionarioId={id}", new { competencia = competenciaAnterior, salario = 4000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await rh.PostAsJsonAsync($"/api/folha-pagamento?funcionarioId={id}", new { competencia = competenciaAtual, salario = 4000, bonus = 500, descontos = 300 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await rh.PutAsJsonAsync($"/api/funcionarios/{id}/vale-alimentacao",
            new { novoValor = 750, dataVigencia = TestUtils.Iso(TestUtils.Hoje), motivo = "Acordo coletivo", versao = funcionario.GetProperty("versao").GetInt32() })).StatusCode);
        var app = _api.Cliente(tokenApp);

        var cadastro = await ApiDeTeste.LerJson(await app.GetAsync("/api/me/cadastro"));
        var holerites = (await ApiDeTeste.LerJson(await app.GetAsync("/api/me/holerites"))).EnumerateArray().ToList();
        var ajustes = (await ApiDeTeste.LerJson(await app.GetAsync("/api/me/ajustes-remuneracao"))).EnumerateArray().ToList();

        Assert.Equal(id, cadastro.GetProperty("id").GetInt32());
        Assert.Equal("Marta Moura", cadastro.GetProperty("nome").GetString());
        Assert.Equal(750m, cadastro.GetProperty("valeAlimentacao").GetDecimal());
        Assert.Equal(new[] { competenciaAtual, competenciaAnterior }, holerites.Select(h => h.GetProperty("competencia").GetString()));
        Assert.Equal(4200m, holerites[0].GetProperty("salarioLiquido").GetDecimal());
        var ajuste = Assert.Single(ajustes);
        Assert.Equal(1, ajuste.GetProperty("tipo").GetInt32());
        Assert.Equal(750m, ajuste.GetProperty("valorNovo").GetDecimal());
    }

    [Fact]
    public async Task MeuCadastro_ContaSemVinculo_Da404EListasVazias()
    {
        var (_, _, token) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var cliente = _api.Cliente(token);

        var cadastro = await cliente.GetAsync("/api/me/cadastro");

        Assert.Equal(HttpStatusCode.NotFound, cadastro.StatusCode);
        Assert.Equal("Sua conta não está ligada a um cadastro de colaborador.", await ApiDeTeste.LerMensagem(cadastro));
        Assert.Empty((await ApiDeTeste.LerJson(await cliente.GetAsync("/api/me/holerites"))).EnumerateArray());
        Assert.Empty((await ApiDeTeste.LerJson(await cliente.GetAsync("/api/me/ajustes-remuneracao"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Cliente().GetAsync("/api/me/cadastro")).StatusCode);
    }

    // ── Falar com o RH ──────────────────────────────────────────────────

    [Fact]
    public async Task MensagemAoRh_FluxoCompleto_EnviaListaERespondeUmaVezSo()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh, "Rita Recursos");
        var rh = _api.Cliente(tokenRh);
        var (_, _, tokenApp) = await _api.CriarColaboradorComAcesso(rh, "Paulo Prado");
        var app = _api.Cliente(tokenApp);

        var envio = await app.PostAsJsonAsync("/api/mensagens-rh", new { assunto = "  Férias  ", mensagem = "Posso tirar\r\nférias em janeiro?\n\n\n\nObrigado" });
        Assert.Equal(HttpStatusCode.Created, envio.StatusCode);
        var mensagem = await ApiDeTeste.LerJson(envio);
        Assert.Equal("Férias", mensagem.GetProperty("assunto").GetString());
        Assert.Equal("Posso tirar\nférias em janeiro?\n\nObrigado", mensagem.GetProperty("mensagem").GetString());
        Assert.Equal(0, mensagem.GetProperty("status").GetInt32());
        Assert.Equal("Paulo Prado", mensagem.GetProperty("usuarioNome").GetString());
        var id = mensagem.GetProperty("id").GetInt32();
        var versao = mensagem.GetProperty("versao").GetInt32();

        var abertas = (await ApiDeTeste.LerJson(await rh.GetAsync("/api/rh/mensagens?status=0"))).EnumerateArray();
        Assert.Contains(abertas, m => m.GetProperty("id").GetInt32() == id);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetAsync("/api/rh/mensagens")).StatusCode);

        var resposta = await rh.PutAsJsonAsync($"/api/rh/mensagens/{id}/resposta", new { resposta = "Pode sim.", versao });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var respondida = await ApiDeTeste.LerJson(resposta);
        Assert.Equal(1, respondida.GetProperty("status").GetInt32());
        Assert.Equal("Rita Recursos", respondida.GetProperty("responsavelNome").GetString());
        Assert.EndsWith("Z", respondida.GetProperty("respondidaEm").GetString());

        // Outra aba (versão velha) ou uma segunda resposta: 409.
        var deNovo = await rh.PutAsJsonAsync($"/api/rh/mensagens/{id}/resposta", new { resposta = "Outra resposta", versao });
        var semVersao = await rh.PutAsJsonAsync($"/api/rh/mensagens/{id}/resposta", new { resposta = "Outra resposta" });
        Assert.Equal(HttpStatusCode.Conflict, deNovo.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, semVersao.StatusCode);
        Assert.Equal("Essa mensagem já foi respondida.", await ApiDeTeste.LerMensagem(semVersao));

        var minhas = (await ApiDeTeste.LerJson(await app.GetAsync("/api/mensagens-rh/minhas"))).EnumerateArray().ToList();
        var minha = Assert.Single(minhas);
        Assert.Equal("Pode sim.", minha.GetProperty("resposta").GetString());
    }

    [Fact]
    public async Task MensagemAoRh_ContaSemColaboradorLigado_Da403()
    {
        var (_, _, token) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);

        var resposta = await _api.Cliente(token).PostAsJsonAsync("/api/mensagens-rh", new { assunto = "Olá", mensagem = "Tudo bem?" });

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("Sua conta não está ligada a um cadastro de colaborador.", await ApiDeTeste.LerMensagem(resposta));
    }

    [Theory]
    [InlineData("Oi", "Mensagem válida", "O assunto precisa ter pelo menos 3 caracteres.")]
    [InlineData("Assunto\ncom quebra", "Mensagem válida", "O assunto contém caracteres invisíveis ou de controle, que não são permitidos.")]
    [InlineData("Assunto", "a\tb", "A mensagem contém caracteres invisíveis ou de controle, que não são permitidos.")]
    [InlineData("Assunto", "  ", "Escreva a mensagem.")]
    public async Task MensagemAoRh_TextoInvalido_Da400(string assunto, string texto, string erro)
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var (_, _, tokenApp) = await _api.CriarColaboradorComAcesso(_api.Cliente(tokenRh));

        var resposta = await _api.Cliente(tokenApp).PostAsJsonAsync("/api/mensagens-rh", new { assunto, mensagem = texto });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(erro, await ApiDeTeste.LerMensagem(resposta));
    }

    [Fact]
    public async Task MensagemAoRh_AcimaDe2000Caracteres_Da400()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var (_, _, tokenApp) = await _api.CriarColaboradorComAcesso(_api.Cliente(tokenRh));

        var resposta = await _api.Cliente(tokenApp).PostAsJsonAsync("/api/mensagens-rh", new { assunto = "Assunto", mensagem = new string('a', 2001) });

        Assert.Equal("A mensagem pode ter no máximo 2000 caracteres.", await ApiDeTeste.LerMensagem(resposta));
    }

    // ── Ponto e justificativa repetidos ─────────────────────────────────

    [Fact]
    public async Task Ponto_SegundoToqueLogoEmSeguida_Da400ComAEspera()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var (_, _, tokenApp) = await _api.CriarColaboradorComAcesso(_api.Cliente(tokenRh));
        var app = _api.Cliente(tokenApp);

        var primeiro = await app.PostAsync("/api/ponto", null);
        var segundo = await app.PostAsync("/api/ponto", null);

        Assert.Equal(HttpStatusCode.Created, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, segundo.StatusCode);
        Assert.Equal("Aguarde 1 minuto entre registros de ponto.", await ApiDeTeste.LerMensagem(segundo));
        Assert.Single((await ApiDeTeste.LerJson(await app.GetAsync("/api/ponto/hoje"))).EnumerateArray());
    }

    [Fact]
    public async Task Justificativa_EnvioRepetido_Da409EVersaoNoPutENoDelete()
    {
        var (_, _, token) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var app = _api.Cliente(token);
        var corpo = new { tipo = 1, data = TestUtils.Iso(TestUtils.Hoje), diaInteiro = true, descricao = "Consulta médica" };

        var primeira = await app.PostAsJsonAsync("/api/ausencias", corpo);
        var repetida = await app.PostAsJsonAsync("/api/ausencias", corpo);
        var tipo99 = await app.PostAsJsonAsync("/api/ausencias", new { tipo = 99, data = TestUtils.Iso(TestUtils.Hoje), descricao = "Tipo errado" });
        var semData = await app.PostAsJsonAsync("/api/ausencias", new { tipo = 0, descricao = "Sem data" });

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal("Tipo de justificativa inválido.", await ApiDeTeste.LerMensagem(tipo99));
        Assert.Equal("Informe a data da ausência.", await ApiDeTeste.LerMensagem(semData));

        var criada = await ApiDeTeste.LerJson(primeira);
        var id = criada.GetProperty("id").GetInt32();
        var versao = criada.GetProperty("versao").GetInt32();
        var edicao = await app.PutAsJsonAsync($"/api/ausencias/{id}", new { tipo = 1, data = TestUtils.Iso(TestUtils.Hoje), descricao = "Consulta médica às 10h", versao });
        Assert.Equal(HttpStatusCode.OK, edicao.StatusCode);

        var cancelarComVersaoVelha = await app.DeleteAsync($"/api/ausencias/{id}?versao={versao}");
        Assert.Equal(HttpStatusCode.Conflict, cancelarComVersaoVelha.StatusCode);
        var cancelar = await app.DeleteAsync($"/api/ausencias/{id}?versao={versao + 1}");
        Assert.Equal(HttpStatusCode.NoContent, cancelar.StatusCode);
    }
}
