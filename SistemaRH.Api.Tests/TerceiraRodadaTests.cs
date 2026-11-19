using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SistemaRH.Api.Common;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Middleware;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Correções da verificação V1 (3ª rodada), em testes sem servidor:
/// caracteres sem atribuição (V1-02), sequências de emoji (V1-03),
/// desligamento de admissão futura (V1-04), logins em paralelo e memória
/// do limitador (V1-05) e senha com emoji.
/// </summary>
public class TerceiraRodadaTests
{
    // Monta o texto a partir dos code points, pra o arquivo não ter
    // caracteres invisíveis escritos direto.
    private static string U(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private const int Fe0f = 0xFE0F;
    private const int Zwj = 0x200D;
    private const int Keycap = 0x20E3;
    private const int Bandeira = 0x1F3F4;
    private const int FimTags = 0xE007F;

    private static readonly RegraTexto[] RegrasComEmoji =
    {
        Textos.CorpoMensagem, Textos.RespostaMensagem, Textos.DescricaoAusencia, Textos.Motivo, Textos.AssuntoMensagem,
    };

    private static string MensagemInvisivel(RegraTexto regra) =>
        $"{regra.Rotulo} contém caracteres invisíveis ou de controle, que não são permitidos.";

    // ── V1-03: só sequências de emoji de verdade ─────────────────────────

    public static TheoryData<string> EmojiValidos => new()
    {
        U(0x2764, Fe0f),                                    // ❤️
        U(0x1F44D, 0x1F3FD),                                // 👍🏽
        U(0x1F468, Zwj, 0x1F469, Zwj, 0x1F467),             // 👨‍👩‍👧
        U(0x1F3F3, Fe0f, Zwj, 0x1F308),                     // 🏳️‍🌈
        U(Bandeira, Zwj, 0x2620, Fe0f),                     // 🏴‍☠️
        U(Bandeira, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, FimTags), // bandeira da Inglaterra
        U('1', Fe0f, Keycap),                               // 1️⃣
        U('#', Fe0f, Keycap),                               // #️⃣
        U('*', Keycap),                                     // *⃣ (sem seletor)
        U(0x00A9, Fe0f),                                    // ©️
        U(0x2122, Fe0f),                                    // ™️
        U(0x21A9, Fe0f),                                    // ↩️
        U(0x2934, Fe0f),                                    // ⤴️
        U(0x2B06, Fe0f),                                    // ⬆️
        U(0x25B6, Fe0f),                                    // ▶️
        U(0x3030, Fe0f),                                    // 〰️
        U(0x2194, 0xFE0E),                                  // ↔︎ (forma de texto)
        U(0x1F469, 0x1F3FD, Zwj, 0x1F4BB),                  // 👩🏽‍💻
        U(0x1F9D1, 0x1F3FB, Zwj, 0x1F91D, Zwj, 0x1F9D1, 0x1F3FF), // 🧑🏻‍🤝‍🧑🏿
        U(0x1F62E, Zwj, 0x1F4A8),                           // 😮‍💨
        U(0x1FAE9),                                         // emoji do Unicode 16 (Cn pro .NET 8)
    };

    [Theory]
    [MemberData(nameof(EmojiValidos))]
    public void Emoji_SequenciasDeVerdade_SaoAceitas(string emoji)
    {
        foreach (var regra in RegrasComEmoji)
        {
            Assert.Equal($"Oi {emoji}", Textos.Validar($"Oi {emoji}", regra));
            Assert.Equal($"Ok{emoji}{emoji}", Textos.Validar($"Ok{emoji}{emoji}", regra));
        }
    }

    public static TheoryData<string> PartesForaDeLugar => new()
    {
        U('a', Fe0f),                                       // seletor depois de letra
        U('a', Fe0f, Fe0f),                                 // o caso do V1: 1 letra + 2 seletores
        U(0x2764, Fe0f, Fe0f),                              // dois seletores na mesma base
        U('O', 'i', 0x2764, 0xFE0E, Fe0f),                  // seletor de texto + de emoji
        "Apro" + U(Zwj) + "vado",                           // ZWJ entre letras
        U(0x1F600, Zwj, 'a'),                               // ZWJ antes de letra
        U('a', Zwj, 0x1F600),                               // ZWJ depois de letra
        U(0x1F44D, Zwj, 0x1F3FD),                           // ZWJ antes de tom de pele
        U(0x1F600, Zwj, Zwj, 0x1F600),                      // ZWJ seguido de ZWJ
        U('a', 0x1F3FD),                                    // tom depois de letra
        U(0x1F44D, 0x1F3FD, 0x1F3FD),                       // dois tons
        U('1', 0x1F3FD),                                    // tom no keycap
        U('a', Keycap),                                     // keycap depois de letra
        U('1', Keycap, Fe0f),                               // seletor depois do keycap
        "Oi" + U(0xE0049, 0xE0047, 0xE004E, 0xE004F, 0xE0052, 0xE0045) + " tudo bem", // texto escondido em tags
        U(0x1F3F3, 0xE0067, 0xE0062, FimTags),              // tags depois de outra bandeira
        U(Bandeira, Fe0f, 0xE0067, 0xE0062, FimTags),       // tags depois do seletor
        U(Bandeira, FimTags),                               // fim das tags sem tag
        U(Bandeira, 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, 0xE0067, 0xE0062, 0xE0073, 0xE0063, FimTags), // 9 tags
        U(Bandeira, 0xE0067, 0xE0062) + " oi",              // tags que não fecham
        U(Bandeira, 0xE0067, 0xE0062),                      // tags no fim do texto, sem fechar
    };

    [Theory]
    [MemberData(nameof(PartesForaDeLugar))]
    public void Emoji_PartesForaDeUmaSequencia_SaoRecusadas(string texto)
    {
        foreach (var regra in RegrasComEmoji)
        {
            var erro = Assert.Throws<BusinessRuleException>(() => Textos.Validar("Mensagem " + texto, regra));
            Assert.Equal(MensagemInvisivel(regra), erro.Message);
        }
    }

    [Fact]
    public void Emoji_MilESeletoresDepoisDeOi_SaoRecusados()
    {
        var texto = "Oi" + string.Concat(Enumerable.Repeat(U(Fe0f), 1998));

        var erro = Assert.Throws<BusinessRuleException>(() => Textos.Validar(texto, Textos.CorpoMensagem));

        Assert.Equal(MensagemInvisivel(Textos.CorpoMensagem), erro.Message);
    }

    [Fact]
    public void Emoji_NoMaximo10ZwjPorSequencia_E8TagsPorBandeira()
    {
        var dezZwj = string.Join(U(Zwj), Enumerable.Repeat(U(0x1F600), 11));
        var onzeZwj = string.Join(U(Zwj), Enumerable.Repeat(U(0x1F600), 12));
        var oitoTags = U(Bandeira, 0xE0061, 0xE0062, 0xE0063, 0xE0064, 0xE0065, 0xE0066, 0xE0067, 0xE0068, FimTags);

        Assert.Equal(dezZwj, Textos.Validar(dezZwj, Textos.CorpoMensagem));
        Assert.Equal($"Oi {oitoTags}", Textos.Validar($"Oi {oitoTags}", Textos.CorpoMensagem));
        Assert.Throws<BusinessRuleException>(() => Textos.Validar(onzeZwj, Textos.CorpoMensagem));
        // Duas sequências de 10 separadas por espaço: cada uma tem a sua conta.
        Assert.Equal($"{dezZwj} {dezZwj}", Textos.Validar($"{dezZwj} {dezZwj}", Textos.CorpoMensagem));
    }

    [Fact]
    public void Tamanho_MinimoNaoContaPartesDeEmoji_MaximoContaTudo()
    {
        // "👍🏽👍🏽": 4 code points, 2 caracteres na tela — abaixo do mínimo de 3.
        var erro = Assert.Throws<BusinessRuleException>(() => Textos.Validar(U(0x1F44D, 0x1F3FD, 0x1F44D, 0x1F3FD), Textos.AssuntoMensagem));
        Assert.Equal("O assunto precisa ter pelo menos 3 caracteres.", erro.Message);
        var tresCoracoes = U(0x2764, Fe0f, 0x2764, Fe0f, 0x2764, Fe0f);
        Assert.Equal(tresCoracoes, Textos.Validar(tresCoracoes, Textos.AssuntoMensagem));

        // 50 × "❤️" = 100 code points: cabe no assunto (máx. 100); com mais uma letra, não.
        var cinquenta = string.Concat(Enumerable.Repeat(U(0x2764, Fe0f), 50));
        Assert.Equal(cinquenta, Textos.Validar(cinquenta, Textos.AssuntoMensagem));
        var acima = Assert.Throws<BusinessRuleException>(() => Textos.Validar(cinquenta + "a", Textos.AssuntoMensagem));
        Assert.Equal("O assunto pode ter no máximo 100 caracteres.", acima.Message);
    }

    // ── V1-02: code points sem caractere e não-caracteres ────────────────

    [Theory]
    [InlineData(0x2065)]
    [InlineData(0xFFF0)]
    [InlineData(0xFFF8)]
    [InlineData(0xE0080)]
    [InlineData(0xE00FF)]
    [InlineData(0xE01F0)]
    [InlineData(0xE0FFF)]
    [InlineData(0xFDD0)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    [InlineData(0x1FFFF)]
    [InlineData(0x10FFFF)]
    public void SemCaractereAtribuido_EhRecusadoEmTodosOsTextosENaSenha(int codePoint)
    {
        var invisivel = U(codePoint);

        var nome = Assert.Throws<BusinessRuleException>(() => Textos.ValidarNomePessoa("V1 Gemeo" + invisivel));
        Assert.Equal(MensagemInvisivel(Textos.NomePessoa), nome.Message);
        Assert.Throws<BusinessRuleException>(() => Textos.Validar("TI" + invisivel, Textos.NomeDepartamento));
        foreach (var regra in RegrasComEmoji)
        {
            var erro = Assert.Throws<BusinessRuleException>(() => Textos.Validar("Mensagem" + invisivel, regra));
            Assert.Equal(MensagemInvisivel(regra), erro.Message);
        }
        var senha = Assert.Throws<BusinessRuleException>(() => Senhas.Validar("senha" + invisivel + "123"));
        Assert.Equal(Senhas.MensagemEmojiOuInvisivel, senha.Message);
    }

    // ── Senha: emoji e invisíveis com mensagem clara ─────────────────────

    [Theory]
    [MemberData(nameof(SenhasComEmojiOuInvisivel))]
    public void Senha_ComEmojiOuInvisivel_TemMensagemPropria(string senha)
    {
        var erro = Assert.Throws<BusinessRuleException>(() => Senhas.Validar(senha, "A senha provisória"));

        Assert.Equal("A senha não pode conter emoji nem caracteres invisíveis.", erro.Message);
    }

    public static TheoryData<string> SenhasComEmojiOuInvisivel => new()
    {
        "Abc" + U(0x2764, Fe0f) + "123",    // Abc❤️123 (o seletor que o teclado põe)
        "Abc" + U(0x2764) + "123",          // Abc❤123
        "senha" + U(0x1F600) + "1",
        "abc" + U(0x200B) + "def",
        "abc" + U(Zwj) + "def",
    };

    // ── V1-04: desligar quem tem admissão futura ─────────────────────────

    [Fact]
    public void Desligamento_AdmissaoFutura_VaiAteAPropriaAdmissao()
    {
        var hoje = Datas.Hoje();
        var admissao = hoje.AddDays(20);

        Assert.Equal(admissao, Datas.ValidarDesligamento(null, admissao));
        Assert.Equal(admissao, Datas.ValidarDesligamento(admissao, admissao));
        var depois = Assert.Throws<BusinessRuleException>(() => Datas.ValidarDesligamento(admissao.AddDays(1), admissao));
        Assert.Equal($"Para quem ainda vai ser admitido, o desligamento é na própria data de admissão ({Datas.Formatar(admissao)}).", depois.Message);
        var antes = Assert.Throws<BusinessRuleException>(() => Datas.ValidarDesligamento(hoje, admissao));
        Assert.Equal($"A data de desligamento não pode ser anterior à admissão ({Datas.Formatar(admissao)}).", antes.Message);

        // Admissão passada: continua de admissão até hoje.
        var passada = hoje.AddYears(-1);
        Assert.Equal(hoje, Datas.ValidarDesligamento(null, passada));
        var futura = Assert.Throws<BusinessRuleException>(() => Datas.ValidarDesligamento(hoje.AddDays(1), passada));
        Assert.Equal("A data de desligamento não pode ser futura.", futura.Message);
        // Admissão hoje: só hoje.
        Assert.Equal(hoje, Datas.ValidarDesligamento(null, hoje));
    }

    // ── V1-05: logins certos em paralelo nunca levam 429 ─────────────────

    [Fact]
    public async Task Login_OitoCertosEmParalelo_TodosEntram()
    {
        var limite = new LimiteTentativasLogin();

        var restantes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            var restante = await limite.ReservarAsync("ana@empresa.com", "10.0.0.1");
            if (restante is null)
            {
                await Task.Delay(20);   // o tempo de conferir o hash
                limite.RegistrarSucesso("ana@empresa.com", "10.0.0.1");
            }
            return restante;
        })));

        Assert.All(restantes, Assert.Null);
    }

    [Fact]
    public async Task Login_VinteCincoPessoasCertasDoMesmoIp_TodasEntram_EVinteCincoErradasParamEm20()
    {
        var limite = new LimiteTentativasLogin();

        var certas = await Task.WhenAll(Enumerable.Range(0, 25).Select(i => Task.Run(async () =>
        {
            var restante = await limite.ReservarAsync($"pessoa{i}@empresa.com", "10.0.0.1");
            if (restante is null)
            {
                await Task.Delay(20);
                limite.RegistrarSucesso($"pessoa{i}@empresa.com", "10.0.0.1");
            }
            return restante;
        })));
        Assert.All(certas, Assert.Null);

        var conferidas = 0;
        var erradas = await Task.WhenAll(Enumerable.Range(0, 25).Select(i => Task.Run(async () =>
        {
            var restante = await limite.ReservarAsync($"alvo{i}@empresa.com", "6.6.6.6");
            if (restante is null)
            {
                Interlocked.Increment(ref conferidas);
                await Task.Delay(5);
                limite.RegistrarFalha($"alvo{i}@empresa.com", "6.6.6.6");
            }
            return restante;
        })));
        Assert.Equal(LimiteTentativasLogin.MaximoFalhasPorIp, conferidas);
        Assert.Equal(5, erradas.Count(r => r > TimeSpan.FromMinutes(14)));
    }

    [Fact]
    public async Task Limitador_ComTetoCheio_RecusaChaveNova_ENuncaApagaBloqueio()
    {
        var relogio = new RelogioFalso(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        // Teto de 4 entradas: dois IPs com uma conta cada.
        var limite = new LimiteTentativasLogin(relogio, limiteDeEntradas: 4);
        for (var i = 0; i < LimiteTentativasLogin.MaximoFalhasPorConta; i++)
        {
            Assert.Null(await limite.ReservarAsync("admin@empresa.com", "6.6.6.6"));
            limite.RegistrarFalha("admin@empresa.com", "6.6.6.6");
        }
        Assert.Null(await limite.ReservarAsync("ana@empresa.com", "7.7.7.7"));
        limite.RegistrarFalha("ana@empresa.com", "7.7.7.7");

        // Cheio de entradas ativas: a chave nova é recusada (falha fechada)...
        relogio.Avancar(TimeSpan.FromSeconds(5));
        Assert.Equal(LimiteTentativasLogin.PausaMemoriaCheia, await limite.ReservarAsync("novo@empresa.com", "8.8.8.8"));
        // ...e o bloqueio que já existia continua valendo.
        Assert.True(await limite.ReservarAsync("admin@empresa.com", "6.6.6.6") > TimeSpan.FromMinutes(14));

        // Passada a janela e o bloqueio, as entradas não guardam mais nada e
        // dão lugar às novas.
        relogio.Avancar(TimeSpan.FromMinutes(16));
        Assert.Null(await limite.ReservarAsync("novo@empresa.com", "8.8.8.8"));
        limite.RegistrarSucesso("novo@empresa.com", "8.8.8.8");
    }
}

/// <summary>V1-01 e os demais itens da 3ª rodada pela API, de ponta a ponta.</summary>
public class TerceiraRodadaIntegracaoTests : IClassFixture<IntegrationTestFactory>
{
    private readonly ApiDeTeste _api;

    public TerceiraRodadaIntegracaoTests(IntegrationTestFactory factory)
    {
        _api = new ApiDeTeste(factory);
    }

    // Cada teste usa o seu IP (X-Forwarded-For): as senhas erradas de um não
    // contam no limite por IP dos outros.
    private HttpClient ClienteDoIp(string ip, string? token = null)
    {
        var cliente = _api.Cliente(token);
        cliente.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return cliente;
    }

    private async Task<HttpResponseMessage> LoginDoIp(string email, string senha, string ip) =>
        await ClienteDoIp(ip).PostAsJsonAsync("/api/auth/login", new { email, senha });

    private async Task<string> TokenDoLogin(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await ApiDeTeste.LerJson(resposta)).GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task V1_01_RhQueDefiniuASenhaNaoEntraComoAdminDepoisDaPromocao()
    {
        const string ip = "203.0.113.31";
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var admin = _api.Cliente(tokenAdmin);

        // 1. O RH cadastra o colaborador já com a senha do app escolhida por ele ("senha123").
        var (_, email, _) = await _api.CriarColaboradorComAcesso(_api.Cliente(tokenRh), "Futura Admin");
        var contaId = _api.LerBanco(db => db.Usuarios.Single(u => u.Email == email).Id);

        // 2. O Admin promove a conta a Admin: sem senha provisória, não dá.
        var semSenha = await admin.PutAsJsonAsync($"/api/usuarios/{contaId}/perfil", new { perfil = 2, perfilAtual = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, semSenha.StatusCode);
        Assert.Equal("Defina uma senha provisória para a conta promovida.", await ApiDeTeste.LerMensagem(semSenha));
        var curta = await admin.PutAsJsonAsync($"/api/usuarios/{contaId}/perfil", new { perfil = 2, perfilAtual = 0, senhaProvisoria = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, curta.StatusCode);
        Assert.Equal("A senha provisória precisa ter pelo menos 6 caracteres.", await ApiDeTeste.LerMensagem(curta));
        Assert.Equal(PerfilUsuario.Funcionario, _api.LerBanco(db => db.Usuarios.Single(u => u.Id == contaId).Perfil));

        var promocao = await admin.PutAsJsonAsync($"/api/usuarios/{contaId}/perfil",
            new { perfil = 2, perfilAtual = 0, senhaProvisoria = "Provisoria#2026" });
        Assert.Equal(HttpStatusCode.OK, promocao.StatusCode);

        // 3. O RH tenta entrar como Admin com a senha que ele escolheu: recusado.
        var doRh = await LoginDoIp(email, "senha123", ip);
        Assert.Equal(HttpStatusCode.BadRequest, doRh.StatusCode);
        Assert.Equal("E-mail ou senha incorretos.", await ApiDeTeste.LerMensagem(doRh));

        // A pessoa promovida entra com a provisória e troca pela dela.
        var tokenProvisorio = await TokenDoLogin(await LoginDoIp(email, "Provisoria#2026", ip));
        Assert.Equal(HttpStatusCode.OK, (await _api.Cliente(tokenProvisorio).GetAsync("/api/usuarios")).StatusCode);
        var troca = await ClienteDoIp(ip, tokenProvisorio).PutAsJsonAsync("/api/auth/senha",
            new { senhaAtual = "Provisoria#2026", novaSenha = "MinhaSenha#1" });
        var tokenNovo = await TokenDoLogin(troca);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Cliente(tokenProvisorio).GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.Cliente(tokenNovo).GetAsync("/api/usuarios")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LoginDoIp(email, "Provisoria#2026", ip)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginDoIp(email, "MinhaSenha#1", ip)).StatusCode);
    }

    [Fact]
    public async Task Promocao_ExigeSenhaQuandoSobe_RebaixamentoNao()
    {
        const string ip = "203.0.113.32";
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var (rhId, rhEmail, _) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var (colabId, _, _) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var admin = _api.Cliente(tokenAdmin);

        var funcionarioParaRh = await admin.PutAsJsonAsync($"/api/usuarios/{colabId}/perfil", new { perfil = 1 });
        var rhParaAdmin = await admin.PutAsJsonAsync($"/api/usuarios/{rhId}/perfil", new { perfil = 2, perfilAtual = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, funcionarioParaRh.StatusCode);
        Assert.Equal("Defina uma senha provisória para a conta promovida.", await ApiDeTeste.LerMensagem(funcionarioParaRh));
        Assert.Equal(HttpStatusCode.BadRequest, rhParaAdmin.StatusCode);

        // Rebaixar não pede senha e não mexe na senha.
        var rebaixa = await admin.PutAsJsonAsync($"/api/usuarios/{rhId}/perfil", new { perfil = 0, perfilAtual = 1 });
        Assert.Equal(HttpStatusCode.OK, rebaixa.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginDoIp(rhEmail, "senha123", ip)).StatusCode);
    }

    [Theory]
    [InlineData("{\"perfil\": 1, \"perfilAtual\": 9}")]
    [InlineData("{\"perfil\": 1, \"perfilAtual\": -1}")]
    [InlineData("{\"perfil\": 1, \"perfilAtual\": \"0\"}")]
    [InlineData("{\"perfil\": 1, \"perfilAtual\": \"Funcionario\"}")]
    public async Task PerfilAtualInvalido_Da400PerfilInvalido(string corpo)
    {
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var (alvoId, _, _) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);

        var resposta = await _api.Cliente(tokenAdmin).PutAsync($"/api/usuarios/{alvoId}/perfil",
            new StringContent(corpo, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("Perfil inválido.", await ApiDeTeste.LerMensagem(resposta));
    }

    [Fact]
    public async Task TrocarSenha_ValidaENaoAceitaAMesma()
    {
        const string ip = "203.0.113.33";
        var (_, email, token) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var cliente = ClienteDoIp(ip, token);

        async Task<(HttpStatusCode, string?)> Trocar(object corpo)
        {
            var resposta = await cliente.PutAsJsonAsync("/api/auth/senha", corpo);
            return (resposta.StatusCode, await ApiDeTeste.LerMensagem(resposta));
        }

        Assert.Equal((HttpStatusCode.BadRequest, "Informe a senha atual."), await Trocar(new { novaSenha = "outra123" }));
        Assert.Equal((HttpStatusCode.BadRequest, "A nova senha precisa ter pelo menos 6 caracteres."), await Trocar(new { senhaAtual = "senha123", novaSenha = "123" }));
        Assert.Equal((HttpStatusCode.BadRequest, "A nova senha precisa ser diferente da atual."), await Trocar(new { senhaAtual = "senha123", novaSenha = "senha123" }));
        Assert.Equal((HttpStatusCode.BadRequest, "A senha não pode conter emoji nem caracteres invisíveis."),
            await Trocar(new { senhaAtual = "senha123", novaSenha = "Abc" + char.ConvertFromUtf32(0x2764) + char.ConvertFromUtf32(0xFE0F) + "123" }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Cliente().PutAsJsonAsync("/api/auth/senha", new { senhaAtual = "senha123", novaSenha = "outra123" })).StatusCode);

        // Nada disso trocou a senha nem derrubou a sessão.
        Assert.Equal(HttpStatusCode.OK, (await _api.Cliente(token).GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginDoIp(email, "senha123", ip)).StatusCode);
    }

    [Fact]
    public async Task TrocarSenha_SenhaAtualErrada_ContaNoLimiteDoLogin()
    {
        const string ip = "203.0.113.34";
        var (_, email, token) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);
        var cliente = ClienteDoIp(ip, token);

        for (var i = 0; i < LimiteTentativasLogin.MaximoFalhasPorConta; i++)
        {
            var errada = await cliente.PutAsJsonAsync("/api/auth/senha", new { senhaAtual = $"errada{i}", novaSenha = "NovaSenha1" });
            Assert.Equal(HttpStatusCode.BadRequest, errada.StatusCode);
            Assert.Equal("Senha atual incorreta.", await ApiDeTeste.LerMensagem(errada));
        }

        // A 6ª, mesmo com a senha certa, e o login daquele IP: bloqueados.
        var certa = await cliente.PutAsJsonAsync("/api/auth/senha", new { senhaAtual = "senha123", novaSenha = "NovaSenha1" });
        Assert.Equal(HttpStatusCode.TooManyRequests, certa.StatusCode);
        Assert.Equal("Muitas tentativas com a senha atual incorreta. Tente novamente em 15 minuto(s).", await ApiDeTeste.LerMensagem(certa));
        Assert.True(int.Parse(certa.Headers.RetryAfter!.ToString()) > 800);
        var login = await LoginDoIp(email, "senha123", ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, login.StatusCode);
        Assert.Equal("Muitas tentativas de login. Tente novamente em 15 minuto(s).", await ApiDeTeste.LerMensagem(login));
        // De outro IP, entra (o bloqueio é por e-mail+IP).
        Assert.Equal(HttpStatusCode.OK, (await LoginDoIp(email, "senha123", "203.0.113.134")).StatusCode);
    }

    [Fact]
    public async Task V1_05_OitoLoginsCertosEmParalelo_TodosDao200()
    {
        var (_, email, _) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => LoginDoIp(email, "senha123", "203.0.113.35")));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task V1_04_AdmissaoFutura_PodeSerDesligadaNaDataDaAdmissao()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var rh = _api.Cliente(tokenRh);
        var (departamentoId, cargoId) = await _api.CriarDepartamentoECargo();
        var admissao = TestUtils.Hoje.AddDays(20);
        var cadastro = await rh.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "Desistiu Antes",
            cpf = TestUtils.GerarCpfValido(),
            dataAdmissao = TestUtils.Iso(admissao),
            departamentoId,
            cargoId,
        });
        Assert.Equal(HttpStatusCode.Created, cadastro.StatusCode);
        var id = (await ApiDeTeste.LerJson(cadastro)).GetProperty("id").GetInt32();

        var hoje = await rh.PostAsJsonAsync($"/api/funcionarios/{id}/desligar", new { dataDesligamento = TestUtils.Iso(TestUtils.Hoje) });
        Assert.Equal(HttpStatusCode.BadRequest, hoje.StatusCode);
        var semData = await rh.PostAsync($"/api/funcionarios/{id}/desligar", null);

        Assert.Equal(HttpStatusCode.OK, semData.StatusCode);
        var funcionario = _api.LerBanco(db => db.Funcionarios.Single(f => f.Id == id));
        Assert.Equal(StatusFuncionario.Desligado, funcionario.Status);
        Assert.Equal(admissao, funcionario.DataDesligamento);
    }

    // ── V4-01: promoção × "Criar acesso ao app" / troca de e-mail ────────
    // As travas (perfis + linha da conta) põem as duas operações em fila; no
    // MySQL a corrida foi conferida à parte. Aqui, as duas ordens possíveis
    // da fila: em nenhuma o RH termina sabendo a senha de uma conta Admin.

    private async Task<(int FuncionarioId, int ContaId, string Email, object Cadastro)> ColaboradorComAcesso(HttpClient rh, string nome)
    {
        var (funcionario, email, _) = await _api.CriarColaboradorComAcesso(rh, nome);
        var contaId = _api.LerBanco(db => db.Usuarios.Single(u => u.Email == email).Id);
        var cadastro = new
        {
            nome,
            email = $"rh-escolheu-{Guid.NewGuid():N}@empresa.com",
            departamentoId = funcionario.GetProperty("departamentoId").GetInt32(),
            cargoId = funcionario.GetProperty("cargoId").GetInt32(),
        };
        return (funcionario.GetProperty("id").GetInt32(), contaId, email, cadastro);
    }

    [Fact]
    public async Task V4_01_PromocaoAntes_AcessoAppETrocaDeEmailDoRhDao403()
    {
        const string ip = "203.0.113.41";
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var rh = _api.Cliente(tokenRh);
        var (funcionarioId, contaId, email, cadastro) = await ColaboradorComAcesso(rh, "Promovida Antes");

        Assert.Equal(HttpStatusCode.OK, (await _api.Cliente(tokenAdmin).PutAsJsonAsync($"/api/usuarios/{contaId}/perfil",
            new { perfil = 2, perfilAtual = 0, senhaProvisoria = "Provisoria#41" })).StatusCode);
        var acesso = await rh.PostAsJsonAsync($"/api/funcionarios/{funcionarioId}/acesso-app", new { senha = "SenhaDoRh#41" });
        var troca = await rh.PutAsJsonAsync($"/api/funcionarios/{funcionarioId}", cadastro);
        var desligar = await rh.PostAsync($"/api/funcionarios/{funcionarioId}/desligar", null);

        Assert.Equal(HttpStatusCode.Forbidden, acesso.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, troca.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, desligar.StatusCode);
        Assert.Equal(email, _api.LerBanco(db => db.Usuarios.Single(u => u.Id == contaId).Email));
        Assert.Equal(HttpStatusCode.BadRequest, (await LoginDoIp(email, "SenhaDoRh#41", ip)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginDoIp(email, "Provisoria#41", ip)).StatusCode);
    }

    [Fact]
    public async Task V4_01_AcessoAppETrocaDeEmailAntes_APromocaoTiraASenhaDoRh()
    {
        const string ip = "203.0.113.42";
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var rh = _api.Cliente(tokenRh);
        var admin = _api.Cliente(tokenAdmin);

        // Senha redefinida pelo RH e, logo depois, a promoção.
        var (funcionarioA, contaA, emailA, _) = await ColaboradorComAcesso(rh, "Senha Antes");
        Assert.Equal(HttpStatusCode.OK, (await rh.PostAsJsonAsync($"/api/funcionarios/{funcionarioA}/acesso-app", new { senha = "SenhaDoRh#42" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/usuarios/{contaA}/perfil",
            new { perfil = 2, perfilAtual = 0, senhaProvisoria = "Provisoria#42" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LoginDoIp(emailA, "SenhaDoRh#42", ip)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginDoIp(emailA, "Provisoria#42", ip)).StatusCode);

        // E-mail trocado pelo RH e, logo depois, a promoção: o login novo só
        // entra com a provisória do Admin.
        var (funcionarioB, contaB, _, cadastroB) = await ColaboradorComAcesso(rh, "Email Antes");
        Assert.Equal(HttpStatusCode.OK, (await rh.PutAsJsonAsync($"/api/funcionarios/{funcionarioB}", cadastroB)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/usuarios/{contaB}/perfil",
            new { perfil = 2, perfilAtual = 0, senhaProvisoria = "Provisoria#43" })).StatusCode);
        var emailNovo = _api.LerBanco(db => db.Usuarios.Single(u => u.Id == contaB).Email);
        Assert.Equal(HttpStatusCode.BadRequest, (await LoginDoIp(emailNovo, "senha123", ip)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginDoIp(emailNovo, "Provisoria#43", ip)).StatusCode);
    }

    [Fact]
    public async Task V1_02_e_V1_03_PelaApi()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var (departamentoId, cargoId) = await _api.CriarDepartamentoECargo();
        var gemeo = await _api.Cliente(tokenRh).PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "V1 Gemeo" + char.ConvertFromUtf32(0xFFF0),
            cpf = TestUtils.GerarCpfValido(),
            dataAdmissao = "2025-03-10",
            departamentoId,
            cargoId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, gemeo.StatusCode);
        Assert.Equal("O nome contém caracteres invisíveis ou de controle, que não são permitidos.", await ApiDeTeste.LerMensagem(gemeo));

        var (_, _, tokenApp) = await _api.CriarColaboradorComAcesso(_api.Cliente(tokenRh));
        var seletores = char.ConvertFromUtf32(0xFE0F) + char.ConvertFromUtf32(0xFE0F);
        var assunto = await _api.Cliente(tokenApp).PostAsJsonAsync("/api/mensagens-rh", new { assunto = "a" + seletores, mensagem = "Mensagem válida" });
        Assert.Equal(HttpStatusCode.BadRequest, assunto.StatusCode);
        Assert.Equal("O assunto contém caracteres invisíveis ou de controle, que não são permitidos.", await ApiDeTeste.LerMensagem(assunto));
    }
}

/// <summary>
/// V3-05: requisição cancelada pelo cliente (o painel cancela os GETs ao
/// sair da tela) não é "Erro inesperado" nem recebe resposta.
/// </summary>
public class CancelamentoPeloClienteTests
{
    private sealed class LoggerDeTeste : ILogger<ExceptionMiddleware>
    {
        public List<(LogLevel Nivel, string Mensagem, Exception? Excecao)> Registros { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Registros.Add((logLevel, formatter(state, exception), exception));
    }

    private static async Task<(DefaultHttpContext Contexto, LoggerDeTeste Logger)> Executar(Exception erro, bool clienteFoiEmbora)
    {
        var logger = new LoggerDeTeste();
        var middleware = new ExceptionMiddleware(_ => throw erro, logger);
        var contexto = new DefaultHttpContext();
        contexto.Request.Method = "GET";
        contexto.Request.Path = "/api/funcionarios";
        contexto.Response.Body = new MemoryStream();
        if (clienteFoiEmbora)
        {
            contexto.RequestAborted = new CancellationToken(canceled: true);
        }

        await middleware.InvokeAsync(contexto);
        return (contexto, logger);
    }

    [Theory]
    [MemberData(nameof(ErrosDeCancelamento))]
    public async Task ClienteCancelou_RegistraComoInformacao_ENaoEscreveResposta(Exception erro)
    {
        var (contexto, logger) = await Executar(erro, clienteFoiEmbora: true);

        Assert.Equal(ExceptionMiddleware.StatusCanceladaPeloCliente, contexto.Response.StatusCode);
        Assert.Equal(0, contexto.Response.Body.Length);
        var registro = Assert.Single(logger.Registros);
        Assert.Equal(LogLevel.Information, registro.Nivel);
        Assert.Null(registro.Excecao);
        Assert.DoesNotContain("Erro inesperado", registro.Mensagem);
        Assert.Contains("cancelada pelo cliente", registro.Mensagem);
    }

    public static TheoryData<Exception> ErrosDeCancelamento => new()
    {
        new OperationCanceledException(),
        new TaskCanceledException(),
        new IOException("The client reset the request stream."),
    };

    [Fact]
    public async Task CancelamentoSemOClienteTerSaido_ContinuaSendoErroInesperado()
    {
        // Um timeout interno (OperationCanceledException com o cliente ainda
        // esperando) é problema de verdade: continua 500 e "Erro inesperado".
        var (contexto, logger) = await Executar(new OperationCanceledException(), clienteFoiEmbora: false);

        Assert.Equal(StatusCodes.Status500InternalServerError, contexto.Response.StatusCode);
        Assert.Contains(logger.Registros, r => r.Nivel == LogLevel.Error && r.Mensagem.Contains("Erro inesperado"));
    }
}

/// <summary>
/// Conferência da sessão a cada requisição (OnTokenValidated): requisição
/// cancelada pelo cliente no meio não vira "fail" do JwtBearerHandler.
/// </summary>
public class ValidacaoDeSessaoTests
{
    private sealed class Registros : ILoggerProvider, ILogger
    {
        public List<(LogLevel Nivel, string Mensagem)> Itens { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Itens.Add((logLevel, formatter(state, exception)));
    }

    // Provedor de serviços que simula o cliente indo embora DURANTE a busca
    // da conta: cancela a requisição e a leitura lança OperationCanceledException.
    private sealed class ClienteSaiNaBusca : IServiceProvider
    {
        private readonly IServiceProvider _servicos;
        private readonly CancellationTokenSource _cancelamento;

        public ClienteSaiNaBusca(IServiceProvider servicos, CancellationTokenSource cancelamento)
        {
            _servicos = servicos;
            _cancelamento = cancelamento;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(Data.AppDbContext))
            {
                _cancelamento.Cancel();
                throw new OperationCanceledException(_cancelamento.Token);
            }
            return _servicos.GetService(serviceType);
        }
    }

    private static (TokenValidatedContext Contexto, Registros Logs, Data.AppDbContext Db, CancellationTokenSource Cancelamento) Montar(int usuarioId, int versao)
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var logs = new Registros();
        var servicos = new ServiceCollection()
            .AddSingleton(db)
            .AddLogging(l => l.AddProvider(logs))
            .BuildServiceProvider();
        var cancelamento = new CancellationTokenSource();
        var http = new DefaultHttpContext { RequestServices = servicos, RequestAborted = cancelamento.Token };
        http.Request.Method = "GET";
        http.Request.Path = "/api/rh/resumo";
        var esquema = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var contexto = new TokenValidatedContext(http, esquema, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", usuarioId.ToString()),
                new Claim(JwtTokenService.ClaimTokenVersao, versao.ToString()),
            })),
        };
        return (contexto, logs, db, cancelamento);
    }

    [Fact]
    public async Task TokenDaVersaoAtual_Passa_EVersaoAntiga_Falha()
    {
        var (contexto, _, db, _) = Montar(1, 0);
        TestUtils.CriarUsuario(db, "ana@empresa.com");

        await ValidacaoDeSessao.ConferirAsync(contexto);
        Assert.Null(contexto.Result);

        var (antigo, _, dbAntigo, _) = Montar(1, 3);
        TestUtils.CriarUsuario(dbAntigo, "ana@empresa.com");
        await ValidacaoDeSessao.ConferirAsync(antigo);
        Assert.NotNull(antigo.Result?.Failure);
    }

    [Fact]
    public async Task ClienteJaTinhaCancelado_EncerraSemErro_ERegistraComoInformacao()
    {
        var (contexto, logs, db, cancelamento) = Montar(1, 0);
        TestUtils.CriarUsuario(db, "ana@empresa.com");
        cancelamento.Cancel();

        await ValidacaoDeSessao.ConferirAsync(contexto);

        Assert.True(contexto.Result!.None);
        Assert.Null(contexto.Result.Failure);
        Assert.DoesNotContain(logs.Itens, r => r.Nivel >= LogLevel.Warning);
        Assert.Contains(logs.Itens, r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("cancelada pelo cliente"));
    }

    [Fact]
    public async Task ClienteCancelaDuranteABusca_EncerraSemErro_ENaoLancaExcecao()
    {
        var (contexto, logs, _, cancelamento) = Montar(1, 0);
        contexto.HttpContext.RequestServices = new ClienteSaiNaBusca(contexto.HttpContext.RequestServices, cancelamento);

        await ValidacaoDeSessao.ConferirAsync(contexto);

        Assert.True(contexto.Result!.None);
        Assert.DoesNotContain(logs.Itens, r => r.Nivel >= LogLevel.Warning);
        Assert.Contains(logs.Itens, r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("cancelada pelo cliente"));
    }
}
