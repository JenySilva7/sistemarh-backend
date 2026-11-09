using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SistemaRH.Api.Dtos;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Testes "de ponta a ponta": fazem requisições HTTP de verdade (só que em
/// memória) contra as rotas da API, exatamente como o app/site fariam.
/// </summary>
public class AuthFlowIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public AuthFlowIntegrationTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FluxoCompleto_RegistrarLoginEAcessarRotaProtegida_Funciona()
    {
        var email = $"{Guid.NewGuid()}@empresa.com";

        var registroResposta = await _client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Nome = "Usuário de Teste",
            Email = email,
            Senha = "senha123",
        });
        Assert.Equal(HttpStatusCode.Created, registroResposta.StatusCode);

        var loginResposta = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = email,
            Senha = "senha123",
        });
        Assert.Equal(HttpStatusCode.OK, loginResposta.StatusCode);

        var corpoLogin = await loginResposta.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(corpoLogin);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corpoLogin!.Token);
        var meResposta = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResposta.StatusCode);

        var usuario = await meResposta.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.Equal(email, usuario!.Email);
    }

    [Fact]
    public async Task RotaProtegida_SemToken_Devolve401()
    {
        var resposta = await _client.GetAsync("/api/ausencias");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Registrar_ComEmailInvalido_Devolve400()
    {
        var resposta = await _client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Nome = "Teste",
            Email = "nao-eh-um-email",
            Senha = "senha123",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Registrar_ComCamposVazios_Devolve400()
    {
        var resposta = await _client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Nome = "",
            Email = "",
            Senha = "",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
