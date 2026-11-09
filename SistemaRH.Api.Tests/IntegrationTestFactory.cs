using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemaRH.Api.Data;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Sobe a API inteira em memória (sem precisar de um processo separado nem
/// de porta de rede) pra testar o fluxo completo: Controller → Middleware
/// de autenticação → Service → banco de dados.
///
/// A única coisa que trocamos é o banco: em vez do SQLite de verdade,
/// usamos o provedor In-Memory, pra cada execução de teste começar com um
/// banco limpo e isolado.
/// </summary>
public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    // IMPORTANTE: o nome precisa ser gerado UMA VEZ só, fora da lambda do
    // AddDbContext. O ASP.NET Core registra DbContextOptions<T> como
    // "Scoped" por padrão, e cada requisição HTTP abre um escopo novo — se o
    // Guid.NewGuid() estivesse dentro da lambda, cada requisição ganharia um
    // banco em memória diferente (vazio), e os dados do registro "sumiriam"
    // na hora do login seguinte.
    private readonly string _nomeDoBancoDeTeste = $"teste-integracao-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descricaoDoDbContext = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descricaoDoDbContext is not null)
            {
                services.Remove(descricaoDoDbContext);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_nomeDoBancoDeTeste));
        });
    }
}
