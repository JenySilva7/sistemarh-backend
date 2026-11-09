using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Cria um AppDbContext usando o provedor "InMemory" do Entity Framework —
/// um banco falso que vive só na memória, criado do zero a cada teste. Isso
/// deixa os testes rápidos e isolados: nenhum teste enxerga dados de outro.
/// </summary>
public static class TestUtils
{
    public static AppDbContext CriarDbContextEmMemoria()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
