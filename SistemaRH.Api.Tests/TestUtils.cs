using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Tests;

/// <summary>
/// Cria um AppDbContext usando o provedor "InMemory" do Entity Framework —
/// um banco falso que vive só na memória, criado do zero a cada teste. Isso
/// deixa os testes rápidos e isolados: nenhum teste enxerga dados de outro.
/// </summary>
public static class TestUtils
{
    public static AppDbContext CriarDbContextEmMemoria() => CriarDbContextEmMemoria(Guid.NewGuid().ToString());

    /// <summary>Dois contextos com o mesmo nome enxergam o mesmo banco (usado pra simular duas requisições).</summary>
    public static AppDbContext CriarDbContextEmMemoria(string nomeDoBanco)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nomeDoBanco)
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>
    /// Cria uma conta direto no banco, já com a senha em hash. O cadastro pela
    /// API (POST /api/auth/register) agora é só de Admin, então os testes
    /// montam as contas por aqui.
    /// </summary>
    public static Usuario CriarUsuario(
        AppDbContext db,
        string email,
        string senha = "senha123",
        PerfilUsuario perfil = PerfilUsuario.Funcionario,
        string nome = "Pessoa de Teste",
        int? funcionarioId = null)
    {
        var usuario = new Usuario
        {
            Nome = nome,
            Email = email,
            Perfil = perfil,
            FuncionarioId = funcionarioId,
            CriadoEm = DateTime.UtcNow,
        };
        usuario.SenhaHash = new PasswordHasher<Usuario>().HashPassword(usuario, senha);
        db.Usuarios.Add(usuario);
        db.SaveChanges();
        return usuario;
    }

    /// <summary>Hoje em Brasília (as faixas de data da API são contadas a partir dele).</summary>
    public static DateTime Hoje => Datas.Hoje();

    /// <summary>Data no formato que o painel e o app mandam ("AAAA-MM-DD").</summary>
    public static string Iso(DateTime data) => data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Como a data de calendário sai no JSON (sem fuso).</summary>
    public static string IsoJson(DateTime data) => data.ToString("yyyy-MM-dd'T'00:00:00", CultureInfo.InvariantCulture);

    /// <summary>CPF aleatório e válido (o banco de integração é compartilhado entre os testes da classe).</summary>
    public static string GerarCpfValido()
    {
        var numeros = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)).ToList();
        for (var quantidade = 9; quantidade <= 10; quantidade++)
        {
            var soma = numeros.Select((n, i) => n * (quantidade + 1 - i)).Sum();
            var resto = soma * 10 % 11;
            numeros.Add(resto == 10 ? 0 : resto);
        }
        var d = string.Concat(numeros);
        return $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}";
    }
}

/// <summary>Relógio controlado pelo teste (ex.: pra "esperar" 1 minuto entre marcações de ponto).</summary>
public class RelogioFalso : TimeProvider
{
    public DateTimeOffset Agora { get; set; }

    public RelogioFalso(DateTimeOffset agora)
    {
        Agora = agora;
    }

    public override DateTimeOffset GetUtcNow() => Agora;

    public void Avancar(TimeSpan tempo) => Agora += tempo;
}
