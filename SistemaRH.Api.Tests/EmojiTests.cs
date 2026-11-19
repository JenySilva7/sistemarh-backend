using System.Net;
using System.Net.Http.Json;
using SistemaRH.Api.Common;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Emoji nos textos longos e no assunto da mensagem ao RH: as partes
/// "invisíveis" de uma sequência de emoji (U+FE0E/FE0F, ZWJ, tom de pele,
/// keycap e tags de bandeira) são aceitas só dentro de uma sequência de
/// emoji de verdade (ver também TerceiraRodadaTests); isoladas, no começo
/// ou depois de espaço, continuam recusadas. Nome, departamento, cargo,
/// e-mail e senha seguem a regra estrita.
/// </summary>
public class EmojiTests
{
    private static readonly RegraTexto[] RegrasComEmoji =
    {
        Textos.CorpoMensagem, Textos.RespostaMensagem, Textos.DescricaoAusencia, Textos.Motivo, Textos.AssuntoMensagem,
    };

    [Theory]
    [InlineData("\u2764\uFE0F")]   // ❤️ (coração + seletor de emoji)
    [InlineData("\U0001F44D\U0001F3FD")]    // 👍🏽 (joinha + tom de pele)
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467")]   // 👨‍👩‍👧 (sequência com ZWJ)
    [InlineData("\U0001F3F3\uFE0F\u200D\U0001F308")]  // 🏳️‍🌈 (bandeira + seletor + ZWJ)
    [InlineData("\U0001F3F4\U000E0067\U000E0062\U000E0073\U000E0063\U000E0074\U000E007F")]   // bandeira com tags de região
    [InlineData("1\uFE0F\u20E3")]     // 1️⃣
    public void Emoji_EhAceitoNosTextosLongosENoAssunto(string emoji)
    {
        foreach (var regra in RegrasComEmoji)
        {
            Assert.Equal($"Obrigado {emoji}", Textos.Validar($"Obrigado {emoji}", regra));
            Assert.Equal($"Ok{emoji}{emoji}", Textos.Validar($"Ok{emoji}{emoji}", regra));
        }
        Assert.Equal($"Linha 1 {emoji}\nLinha 2", Textos.Validar($"Linha 1 {emoji}\r\nLinha 2", Textos.CorpoMensagem));
    }

    [Fact]
    public void Emoji_SoComEmojiDeVerdade_PassaNoMinimo()
    {
        // O mínimo conta os caracteres na tela: 3 corações (6 code points) passam, 2 não.
        Assert.Equal("\u2764\uFE0F\u2764\uFE0F\u2764\uFE0F", Textos.Validar("\u2764\uFE0F\u2764\uFE0F\u2764\uFE0F", Textos.AssuntoMensagem));
        Assert.Throws<BusinessRuleException>(() => Textos.Validar("\u2764\uFE0F\u2764\uFE0F", Textos.AssuntoMensagem));
    }

    // ── Tamanho contado por caractere (code point), não por unidade UTF-16 ──

    [Fact]
    public void Tamanho_EmojiForaDoBmpContaComoUmCaractere()
    {
        // 99 letras + 👍 = 100 caracteres (101 unidades UTF-16): cabe no assunto (máx. 100).
        var assunto = new string('a', 99) + "\U0001F44D";
        Assert.Equal(assunto, Textos.Validar(assunto, Textos.AssuntoMensagem));

        var acimaDoMaximo = new string('a', 100) + "\U0001F44D";
        var erroMaximo = Assert.Throws<BusinessRuleException>(() => Textos.Validar(acimaDoMaximo, Textos.AssuntoMensagem));
        Assert.Equal("O assunto pode ter no máximo 100 caracteres.", erroMaximo.Message);
    }

    [Fact]
    public void Tamanho_DoisEmojisNaoAtingemOMinimoDe3()
    {
        // "👍👍" são 2 caracteres (4 unidades UTF-16): abaixo do mínimo de 3.
        var erro = Assert.Throws<BusinessRuleException>(() => Textos.Validar("\U0001F44D\U0001F44D", Textos.AssuntoMensagem));
        Assert.Equal("O assunto precisa ter pelo menos 3 caracteres.", erro.Message);
        Assert.Equal("\U0001F44D\U0001F44D\U0001F44D", Textos.Validar("\U0001F44D\U0001F44D\U0001F44D", Textos.AssuntoMensagem));
    }

    [Theory]
    [InlineData("\uFE0F")]                         // seletor sozinho
    [InlineData("Oi \uFE0F")]                      // depois de espaço
    [InlineData("Oi\n\uFE0F tudo")]              // depois de quebra de linha
    [InlineData("a\u200D")]                        // ZWJ no fim (não une nada)
    [InlineData("\u200D\u200D\u200D")]         // texto só de ZWJ
    [InlineData("\u200Dabc")]                      // ZWJ no começo
    [InlineData("Oi \u200D tudo")]                 // ZWJ entre espaços
    [InlineData("a\u200D\u200Db")]               // ZWJ seguido de ZWJ
    [InlineData("\U0001F3FD oi")]                  // tom de pele no começo
    [InlineData("oi \U000E0067")]                  // tag de bandeira solta
    [InlineData("oi\u200B")]                       // zero-width space continua recusado
    public void ParteDeEmojiIsoladaOuNoComeco_EhRecusada(string texto)
    {
        foreach (var regra in RegrasComEmoji)
        {
            var erro = Assert.Throws<BusinessRuleException>(() => Textos.Validar(texto, regra));
            Assert.Equal($"{regra.Rotulo} contém caracteres invisíveis ou de controle, que não são permitidos.", erro.Message);
        }
    }

    [Theory]
    [InlineData("\u2764\uFE0F")]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467")]
    [InlineData("\U0001F3F3\uFE0F\u200D\U0001F308")]
    public void CamposEstritos_ContinuamRecusandoAsPartesDeEmoji(string emoji)
    {
        Assert.Throws<BusinessRuleException>(() => Textos.ValidarNomePessoa($"Ana Souza {emoji}"));
        Assert.Throws<BusinessRuleException>(() => Textos.Validar($"TI {emoji}", Textos.NomeDepartamento));
        Assert.Throws<BusinessRuleException>(() => Textos.Validar($"Dev {emoji}", Textos.NomeCargo));
        Assert.Throws<BusinessRuleException>(() => Senhas.Validar($"senha1{emoji}"));
        Assert.Throws<BusinessRuleException>(() => Emails.Normalizar($"ana{emoji}@empresa.com"));
    }
}

public class EmojiIntegracaoTests : IClassFixture<IntegrationTestFactory>
{
    private readonly ApiDeTeste _api;

    public EmojiIntegracaoTests(IntegrationTestFactory factory)
    {
        _api = new ApiDeTeste(factory);
    }

    [Fact]
    public async Task MensagemAoRh_ComEmoji_EhGravadaComoFoiEscrita()
    {
        var (_, _, tokenRh) = await _api.CriarContaELogar(PerfilUsuario.Rh);
        var (_, _, tokenApp) = await _api.CriarColaboradorComAcesso(_api.Cliente(tokenRh));
        var assunto = "Obrigado \u2764\uFE0F";
        var mensagem = "Minha família \U0001F468\u200D\U0001F469\u200D\U0001F467 agradece \U0001F44D\U0001F3FD\n\nOrgulho \U0001F3F3\uFE0F\u200D\U0001F308";

        var resposta = await _api.Cliente(tokenApp).PostAsJsonAsync("/api/mensagens-rh", new { assunto, mensagem });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var json = await ApiDeTeste.LerJson(resposta);
        Assert.Equal(assunto, json.GetProperty("assunto").GetString());
        Assert.Equal(mensagem, json.GetProperty("mensagem").GetString());
    }

    [Fact]
    public async Task Justificativa_SoComZwj_EhRecusada()
    {
        var (_, _, token) = await _api.CriarContaELogar(PerfilUsuario.Funcionario);

        var resposta = await _api.Cliente(token).PostAsJsonAsync("/api/ausencias", new
        {
            tipo = 0,
            data = TestUtils.Iso(TestUtils.Hoje),
            descricao = "\u200D\u200D\u200D\u200D",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("A descrição contém caracteres invisíveis ou de controle, que não são permitidos.", await ApiDeTeste.LerMensagem(resposta));
    }
}
