using System.Net;
using System.Net.Http.Json;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Models;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Testes "de ponta a ponta": fazem requisições HTTP de verdade (só que em
/// memória) contra as rotas da API, exatamente como o app/site fariam.
/// </summary>
public class AuthFlowIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly ApiDeTeste _api;

    public AuthFlowIntegrationTests(IntegrationTestFactory factory)
    {
        _api = new ApiDeTeste(factory);
    }

    [Fact]
    public async Task FluxoCompleto_AdminCriaContaQueFazLoginEAcessaRotaProtegida()
    {
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);
        var email = $"{Guid.NewGuid():N}@empresa.com";

        var registroResposta = await _api.Cliente(tokenAdmin).PostAsJsonAsync("/api/auth/register", new
        {
            nome = "Usuário de Teste",
            email,
            senha = "senha123",
        });
        Assert.Equal(HttpStatusCode.Created, registroResposta.StatusCode);

        var loginResposta = await _api.TentarLogin(email, "senha123");
        Assert.Equal(HttpStatusCode.OK, loginResposta.StatusCode);

        var corpoLogin = await loginResposta.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(corpoLogin);

        var meResposta = await _api.Cliente(corpoLogin!.Token).GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResposta.StatusCode);

        var usuario = await meResposta.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.Equal(email, usuario!.Email);
    }

    [Fact]
    public async Task RotaProtegida_SemToken_Devolve401ComMensagem()
    {
        var resposta = await _api.Cliente().GetAsync("/api/ausencias");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("Faça login para continuar.", await ApiDeTeste.LerMensagem(resposta));
    }

    [Fact]
    public async Task Registrar_ComEmailInvalido_Devolve400()
    {
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);

        var resposta = await _api.Cliente(tokenAdmin).PostAsJsonAsync("/api/auth/register", new
        {
            nome = "Teste da Silva",
            email = "nao-eh-um-email",
            senha = "senha123",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("Informe um e-mail válido, no formato usuario@dominio.com.", await ApiDeTeste.LerMensagem(resposta));
    }

    [Fact]
    public async Task Registrar_ComCamposVazios_Devolve400()
    {
        var (_, _, tokenAdmin) = await _api.CriarContaELogar(PerfilUsuario.Admin);

        var resposta = await _api.Cliente(tokenAdmin).PostAsJsonAsync("/api/auth/register", new
        {
            nome = "",
            email = "",
            senha = "",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("O nome é obrigatório.", await ApiDeTeste.LerMensagem(resposta));
    }
}
