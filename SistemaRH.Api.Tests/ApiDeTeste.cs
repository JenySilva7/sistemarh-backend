using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Atalhos dos testes de integração: clientes HTTP com token, acesso direto
/// ao banco em memória da API de teste e criação rápida de contas e
/// colaboradores (o register público não existe mais).
/// </summary>
public class ApiDeTeste
{
    private readonly IntegrationTestFactory _factory;

    public ApiDeTeste(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    public HttpClient Cliente(string? token = null)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public async Task UsarBanco(Func<AppDbContext, Task> acao)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await acao(db);
        await db.SaveChangesAsync();
    }

    public T LerBanco<T>(Func<AppDbContext, T> consulta)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return consulta(db);
    }

    public static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

    public static async Task<string?> LerMensagem(HttpResponseMessage resposta) =>
        (await LerJson(resposta)).GetProperty("mensagem").GetString();

    public async Task<HttpResponseMessage> TentarLogin(string email, string senha) =>
        await Cliente().PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Senha = senha });

    public async Task<string> Logar(string email, string senha = "senha123")
    {
        var resposta = await TentarLogin(email, senha);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await LerJson(resposta)).GetProperty("token").GetString()!;
    }

    /// <summary>Cria uma conta com o perfil pedido direto no banco e devolve (id, token).</summary>
    public async Task<(int Id, string Email, string Token)> CriarContaELogar(PerfilUsuario perfil, string nome = "Pessoa de Teste")
    {
        var email = $"{perfil.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@empresa.com";
        var id = 0;
        await UsarBanco(db =>
        {
            id = TestUtils.CriarUsuario(db, email, perfil: perfil, nome: nome).Id;
            return Task.CompletedTask;
        });
        return (id, email, await Logar(email));
    }

    public async Task<(int DepartamentoId, int CargoId)> CriarDepartamentoECargo(decimal salarioBase = 4000m)
    {
        var departamento = new Departamento { Nome = $"Depto {Guid.NewGuid():N}"[..30] };
        var cargo = new Cargo { Nome = $"Cargo {Guid.NewGuid():N}"[..30], SalarioBase = salarioBase };
        await UsarBanco(db =>
        {
            db.Departamentos.Add(departamento);
            db.Cargos.Add(cargo);
            return Task.CompletedTask;
        });
        return (departamento.Id, cargo.Id);
    }

    /// <summary>
    /// O RH cadastra um colaborador já com acesso ao app (senha "senha123") e
    /// ele entra no app. Devolve o JSON do cadastro e o token do app.
    /// </summary>
    public async Task<(JsonElement Funcionario, string Email, string TokenApp)> CriarColaboradorComAcesso(HttpClient rh, string nome = "Carla Dias")
    {
        var (departamentoId, cargoId) = await CriarDepartamentoECargo();
        var email = $"colab-{Guid.NewGuid():N}@empresa.com";
        var resposta = await rh.PostAsJsonAsync("/api/funcionarios", new
        {
            nome,
            cpf = TestUtils.GerarCpfValido(),
            email,
            dataAdmissao = "2025-03-10",
            departamentoId,
            cargoId,
            senhaAcessoApp = "senha123",
        });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await LerJson(resposta), email, await Logar(email));
    }
}
