using SistemaRH.Api.Data;
using SistemaRH.Api.Models;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>
/// O MySQL devolve DATETIME sem fuso (DateTimeKind.Unspecified). Aqui a
/// gente grava valores "Unspecified" de propósito, pra simular isso, e
/// confere que os instantes voltam marcados como UTC (saem com "Z" no JSON)
/// enquanto as datas de calendário continuam sem fuso.
/// </summary>
public class TimestampUtcTests
{
    private static DateTime SemFuso(int ano, int mes, int dia, int hora = 0, int minuto = 0) =>
        new(ano, mes, dia, hora, minuto, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task Instantes_AoLerDoBanco_VoltamComoUtc()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var usuario = new Usuario { Nome = "Ana", Email = "ana@empresa.com", CriadoEm = SemFuso(2026, 11, 1, 12) };
        db.Usuarios.Add(usuario);
        db.RegistrosPonto.Add(new RegistroPonto { Usuario = usuario, Tipo = TipoRegistroPonto.Entrada, RegistradoEm = SemFuso(2026, 11, 13, 15, 50) });
        db.Ausencias.Add(new Ausencia { Usuario = usuario, Data = SemFuso(2026, 11, 10), Descricao = "Consulta", CriadoEm = SemFuso(2026, 11, 13, 16) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var registro = db.RegistrosPonto.Single();
        var ausencia = db.Ausencias.Single();
        var usuarioLido = db.Usuarios.Single();

        Assert.Equal(DateTimeKind.Utc, registro.RegistradoEm.Kind);
        Assert.Equal(SemFuso(2026, 11, 13, 15, 50), registro.RegistradoEm); // mesmo valor, só o Kind muda
        Assert.Equal(DateTimeKind.Utc, ausencia.CriadoEm.Kind);
        Assert.Equal(DateTimeKind.Utc, usuarioLido.CriadoEm.Kind);
    }

    [Fact]
    public async Task DatasDeCalendario_AoLerDoBanco_ContinuamSemFuso()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var departamento = new Departamento { Nome = "TI" };
        var cargo = new Cargo { Nome = "Dev" };
        var funcionario = new Funcionario
        {
            Nome = "Carlos",
            Cpf = "1",
            DataAdmissao = SemFuso(2024, 1, 1),
            DataNascimento = SemFuso(1990, 5, 20),
            Departamento = departamento,
            Cargo = cargo,
            CriadoEm = SemFuso(2026, 11, 1),
        };
        db.Funcionarios.Add(funcionario);
        db.LancamentosBancoHoras.Add(new LancamentoBancoHoras { Funcionario = funcionario, DataRegistro = SemFuso(2026, 8, 10), QuantidadeMinutos = 60, CriadoEm = SemFuso(2026, 8, 10, 18) });
        db.Ausencias.Add(new Ausencia { UsuarioId = 1, Data = SemFuso(2026, 11, 10), Descricao = "Consulta" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var funcionarioLido = db.Funcionarios.Single();
        var lancamento = db.LancamentosBancoHoras.Single();
        var ausencia = db.Ausencias.Single();

        Assert.Equal(DateTimeKind.Unspecified, funcionarioLido.DataAdmissao.Kind);
        Assert.Equal(DateTimeKind.Unspecified, funcionarioLido.DataNascimento!.Value.Kind);
        Assert.Equal(DateTimeKind.Unspecified, lancamento.DataRegistro.Kind);
        Assert.Equal(DateTimeKind.Unspecified, ausencia.Data.Kind);
        // ...mas o CriadoEm dessas mesmas entidades é instante, então vira UTC.
        Assert.Equal(DateTimeKind.Utc, funcionarioLido.CriadoEm.Kind);
        Assert.Equal(DateTimeKind.Utc, lancamento.CriadoEm.Kind);
    }

    [Fact]
    public void ConversorUtc_SoTrocaOKindAoLer()
    {
        var valorDoBanco = SemFuso(2026, 11, 13, 15, 50);

        var lido = (DateTime)AppDbContext.ConversorUtc.ConvertFromProvider(valorDoBanco)!;

        Assert.Equal(DateTimeKind.Utc, lido.Kind);
        Assert.Equal(valorDoBanco.Ticks, lido.Ticks);
    }
}
