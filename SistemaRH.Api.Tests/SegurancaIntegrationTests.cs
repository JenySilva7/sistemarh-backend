using System.Net;
using System.Net.Http.Json;
using System.Text;
using SistemaRH.Api.Models;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Autenticação, sessão e erros pela API de verdade (em memória): register só
/// de Admin, versão do token (logout, troca de senha, desligamento, perfil),
/// limite de tentativas de login, cabeçalhos de segurança e mensagens de erro
/// sempre no formato { "mensagem" } em português.
/// </summary>
public class SegurancaIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly ApiDeTeste _api;

    public SegurancaIntegrationTests(IntegrationTestFactory factory)
    {
        _api = new ApiDeTeste(factory);
    }

    // ── Register ────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_SoAdminConsegue()
    {
        var (_, _, tokenFuncionario) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        object Corpo() => new { nome = "Intruso da Silva", email = $"intruso-{Guid.NewGuid():N}@x.com", senha = "senha123" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Cliente().PostAsJsonAsync("/api/auth/register", Corpo())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.Cliente(tokenFuncionario).PostAsJsonAsync("/api/auth/register", Corpo())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.Cliente(tokenRh).PostAsJsonAsync("/api/auth/register", Corpo())).StatusCode);
    }

    [Fact]
    public async Task Register_PorAdmin_IgnoraCamposQueNaoSaoDoContrato()
    {
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var email = $"nova-{Guid.NewGuid():N}@empresa.com";

        var resposta = await _api.Cliente(tokenAdmin).PostAsJsonAsync("/api/auth/register", new
        {
            nome = "Nova Pessoa",
            email,
            senha = "senha123",
            perfil = 1,
            saldoBancoHorasMinutos = 999999,
            cargo = "CEO",
            departamento = "Diretoria",
        });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var json = await ApiDeTeste.LerJson(resposta);
        Assert.Equal(1, json.GetProperty("perfil").GetInt32());
        Assert.False(json.TryGetProperty("token", out _));
        var conta = _api.LerBanco(db => db.Usuarios.Single(u => u.Email == email));
        Assert.Equal(0, conta.SaldoBancoHorasMinutos);
        Assert.Equal(string.Empty, conta.Cargo);
        Assert.Equal(string.Empty, conta.Departamento);
    }

    // ── Versão do token ─────────────────────────────────────────────────

    [Fact]
    public async Task Logout_InvalidaTodosOsTokensDaConta()
    {
        var (_, email, token1) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var token2 = await _api.Logar(email); // outra aba / outro aparelho

        var logout = await _api.Cliente(token1).PostAsJsonAsync("/api/auth/logout", new { });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        foreach (var token in new[] { token1, token2 })
        {
            var resposta = await _api.Cliente(token).GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
            Assert.Equal("Sua sessão foi encerrada. Entre novamente.", await ApiDeTeste.LerMensagem(resposta));
        }
        var novoToken = await _api.Logar(email);
        Assert.Equal(HttpStatusCode.OK, (await _api.Cliente(novoToken).GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task RedefinirSenhaDoApp_DerrubaASessaoAberta()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var rh = _api.Cliente(tokenRh);
        var (funcionario, email, tokenApp) = await _api.CriarColaboradorComAcesso(rh);

        var redefinicao = await rh.PostAsJsonAsync($"/api/funcionarios/{funcionario.GetProperty("id").GetInt32()}/acesso-app", new { senha = "novaSenha1" });

        Assert.Equal(HttpStatusCode.OK, redefinicao.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Cliente(tokenApp).GetAsync("/api/ponto/hoje")).StatusCode);
        await _api.Logar(email, "novaSenha1");
    }

    [Fact]
    public async Task Desligamento_BloqueiaTodasAsRotasDoAppNaHora()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var rh = _api.Cliente(tokenRh);
        var (funcionario, _, tokenApp) = await _api.CriarColaboradorComAcesso(rh);
        var app = _api.Cliente(tokenApp);
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/ponto/hoje")).StatusCode);

        // Sem corpo e sem Content-Type: o desligamento usa a data de hoje.
        var desligar = await rh.PostAsync($"/api/funcionarios/{funcionario.GetProperty("id").GetInt32()}/desligar", null);
        Assert.Equal(HttpStatusCode.OK, desligar.StatusCode);

        var ponto = await app.PostAsync("/api/ponto", null);
        var ausencia = await app.PostAsJsonAsync("/api/ausencias", new { tipo = 1, data = TestUtils.Iso(TestUtils.Hoje), descricao = "Depois de sair" });
        var mensagem = await app.PostAsJsonAsync("/api/mensagens-rh", new { assunto = "Oi", mensagem = "Ainda tenho acesso?" });
        Assert.Equal(HttpStatusCode.Unauthorized, ponto.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ausencia.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, mensagem.StatusCode);
        Assert.Equal("Seu acesso foi desativado. Procure o RH.", await ApiDeTeste.LerMensagem(ponto));
    }

    [Fact]
    public async Task TokenDeContaApagada_EhRecusado()
    {
        var (id, email, token) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        await _api.UsarBanco(db =>
        {
            db.Usuarios.Remove(db.Usuarios.Single(u => u.Id == id));
            return Task.CompletedTask;
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Cliente(token).GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.TentarLogin(email, "senha123")).StatusCode);
    }

    // ── Login ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_CincoFalhasNoMesmoEmail_BloqueiaPor15Minutos()
    {
        var (_, email, _) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);

        for (var i = 0; i < 5; i++)
        {
            var falha = await _api.TentarLogin(email, "senhaErrada");
            Assert.Equal(HttpStatusCode.BadRequest, falha.StatusCode);
            Assert.Equal("E-mail ou senha incorretos.", await ApiDeTeste.LerMensagem(falha));
        }

        // Bloqueado: nem a senha certa entra até o bloqueio acabar.
        var bloqueado = await _api.TentarLogin(email, "senha123");
        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueado.StatusCode);
        Assert.Equal("Muitas tentativas de login. Tente novamente em 15 minuto(s).", await ApiDeTeste.LerMensagem(bloqueado));
        Assert.NotNull(bloqueado.Headers.RetryAfter);

        // O bloqueio é por e-mail: outra conta continua entrando.
        var (_, outroEmail, _) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        Assert.Equal(HttpStatusCode.OK, (await _api.TentarLogin(outroEmail, "senha123")).StatusCode);
    }

    [Fact]
    public async Task Login_EmailInexistenteTambemContaParaOBloqueio()
    {
        var email = $"nao-existe-{Guid.NewGuid():N}@empresa.com";
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _api.TentarLogin(email, "qualquer1")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await _api.TentarLogin(email, "qualquer1")).StatusCode);
    }

    [Theory]
    [InlineData("zédalima@gmail.com")]
    [InlineData("ze\u0000lima@gmail.com")]
    [InlineData("nao-eh-email")]
    public async Task Login_ComEmailNaoAscii_RecusaComAMensagemGenerica(string email)
    {
        var resposta = await _api.TentarLogin(email, "senha123");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("E-mail ou senha incorretos.", await ApiDeTeste.LerMensagem(resposta));
    }

    // ── Perfis e contas ─────────────────────────────────────────────────

    [Fact]
    public async Task Usuarios_RhListaMasSoAdminMudaPerfil_EOTokenDoAlteradoCai()
    {
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var (idRh, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh, "Rita Recursos");
        var (idColab, _, tokenColab) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);

        var lista = await _api.Cliente(tokenRh).GetAsync("/api/usuarios");
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        var item = (await ApiDeTeste.LerJson(lista)).EnumerateArray().Single(u => u.GetProperty("id").GetInt32() == idRh);
        Assert.Equal("Rita Recursos", item.GetProperty("nome").GetString());
        Assert.Equal(1, item.GetProperty("perfil").GetInt32());
        Assert.False(item.GetProperty("funcionarioDesligado").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.Cliente(tokenColab).GetAsync("/api/usuarios")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.Cliente(tokenRh).PutAsJsonAsync($"/api/usuarios/{idColab}/perfil", new { perfil = 2 })).StatusCode);

        var promocao = await _api.Cliente(tokenAdmin).PutAsJsonAsync($"/api/usuarios/{idColab}/perfil", new { perfil = 1, senhaProvisoria = "provisoria1" });
        Assert.Equal(HttpStatusCode.OK, promocao.StatusCode);
        Assert.Equal(1, (await ApiDeTeste.LerJson(promocao)).GetProperty("perfil").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Cliente(tokenColab).GetAsync("/api/auth/me")).StatusCode);

        var perfilInvalido = await _api.Cliente(tokenAdmin).PutAsJsonAsync($"/api/usuarios/{idColab}/perfil", new { perfil = 7 });
        Assert.Equal(HttpStatusCode.BadRequest, perfilInvalido.StatusCode);
        Assert.Equal("Perfil inválido.", await ApiDeTeste.LerMensagem(perfilInvalido));
    }

    [Fact]
    public async Task Cargos_SoRhOuAdmin()
    {
        var (_, _, tokenColab) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);

        var colaborador = await _api.Cliente(tokenColab).GetAsync("/api/cargos");

        Assert.Equal(HttpStatusCode.Forbidden, colaborador.StatusCode);
        Assert.Equal("Você não tem permissão para acessar este recurso.", await ApiDeTeste.LerMensagem(colaborador));
        Assert.Equal(HttpStatusCode.OK, (await _api.Cliente(tokenRh).GetAsync("/api/cargos")).StatusCode);
    }

    [Fact]
    public async Task AusenciaDeOutraPessoa_Da404IgualAInexistente()
    {
        var (_, _, tokenAna) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var (_, _, tokenBia) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var criada = await _api.Cliente(tokenAna).PostAsJsonAsync("/api/ausencias", new { tipo = 0, data = TestUtils.Iso(TestUtils.Hoje), descricao = "Consulta médica" });
        var id = (await ApiDeTeste.LerJson(criada)).GetProperty("id").GetInt32();

        var deOutra = await _api.Cliente(tokenBia).GetAsync($"/api/ausencias/{id}");
        var inexistente = await _api.Cliente(tokenBia).GetAsync("/api/ausencias/999999");

        Assert.Equal(HttpStatusCode.NotFound, deOutra.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(await ApiDeTeste.LerMensagem(inexistente), await ApiDeTeste.LerMensagem(deOutra));
    }

    // ── Erros e cabeçalhos ──────────────────────────────────────────────

    [Fact]
    public async Task ContentTypeErrado_Da415ComMensagemEmPortugues()
    {
        var resposta = await _api.Cliente().PostAsync("/api/auth/login", new StringContent("email=a", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resposta.StatusCode);
        Assert.Equal("Formato de conteúdo não suportado. Envie os dados em JSON (Content-Type: application/json).", await ApiDeTeste.LerMensagem(resposta));
    }

    [Fact]
    public async Task CorpoVazioOuJsonQuebrado_DaMensagemEmPortugues()
    {
        var vazio = await _api.Cliente().PostAsync("/api/auth/login", new StringContent("", Encoding.UTF8, "application/json"));
        var nulo = await _api.Cliente().PostAsync("/api/auth/login", new StringContent("null", Encoding.UTF8, "application/json"));
        var quebrado = await _api.Cliente().PostAsync("/api/auth/login", new StringContent("{\"email\":", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, vazio.StatusCode);
        Assert.Equal("O corpo da requisição está vazio. Envie os dados em JSON.", await ApiDeTeste.LerMensagem(vazio));
        Assert.Equal("O corpo da requisição está vazio. Envie os dados em JSON.", await ApiDeTeste.LerMensagem(nulo));
        Assert.Equal(HttpStatusCode.BadRequest, quebrado.StatusCode);
        Assert.DoesNotContain("could not", await ApiDeTeste.LerMensagem(quebrado));
    }

    [Fact]
    public async Task ErroDeBindingNaQuery_NaoRepeteOValorEnviado()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);

        var resposta = await _api.Cliente(tokenRh).GetAsync("/api/funcionarios?departamentoId=1;DROP%20TABLE%20Funcionarios");
        var status = await _api.Cliente(tokenRh).GetAsync("/api/funcionarios?status=99");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var mensagem = await ApiDeTeste.LerMensagem(resposta);
        Assert.Equal("Departamento está em formato inválido.", mensagem);
        Assert.Equal(HttpStatusCode.BadRequest, status.StatusCode);
    }

    [Fact]
    public async Task PontoDoRh_ComDataForaDaFaixa_Da400()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);

        var resposta = await _api.Cliente(tokenRh).GetAsync("/api/rh/ponto?data=9999-12-31");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.StartsWith("Data fora do período permitido", await ApiDeTeste.LerMensagem(resposta));
    }

    [Fact]
    public async Task TodaResposta_TemCabecalhosDeSegurancaESemServer()
    {
        var resposta = await _api.Cliente().GetAsync("/api/departamentos");

        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resposta.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", resposta.Headers.GetValues("Referrer-Policy").Single());
        Assert.False(resposta.Headers.Contains("Server"));
    }

    [Fact]
    public async Task RotaInexistente_Da404ComMensagem()
    {
        var resposta = await _api.Cliente().GetAsync("/api/nao-existe");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Recurso não encontrado.", await ApiDeTeste.LerMensagem(resposta));
    }
}
