using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class RhServiceTests
{
    private static RhService CriarServico(out AppDbContext db)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        return new RhService(db);
    }

    private static Usuario CriarUsuario(AppDbContext db, string nome, int? funcionarioId = null)
    {
        var usuario = new Usuario { Nome = nome, Email = $"{nome.ToLower().Replace(' ', '.')}@empresa.com", FuncionarioId = funcionarioId };
        db.Usuarios.Add(usuario);
        db.SaveChanges();
        return usuario;
    }

    private static Ausencia CriarAusencia(AppDbContext db, Usuario usuario, StatusAusencia status = StatusAusencia.Enviada, DateTime? criadoEm = null)
    {
        var ausencia = new Ausencia
        {
            UsuarioId = usuario.Id,
            Tipo = TipoAusencia.AtestadoMedico,
            Data = new DateTime(2026, 11, 10),
            Descricao = "Consulta médica",
            Status = status,
            CriadoEm = criadoEm ?? DateTime.UtcNow,
        };
        db.Ausencias.Add(ausencia);
        db.SaveChanges();
        return ausencia;
    }

    private static DateTime Utc(int ano, int mes, int dia, int hora, int minuto) => new(ano, mes, dia, hora, minuto, 0, DateTimeKind.Utc);

    // ── Ausências ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarAusencias_DevolveDeTodosOsUsuariosComQuemEnviou_MaisRecentesPrimeiro()
    {
        var service = CriarServico(out var db);
        var ana = CriarUsuario(db, "Ana", funcionarioId: 7);
        var bruno = CriarUsuario(db, "Bruno");
        CriarAusencia(db, ana, criadoEm: Utc(2026, 11, 1, 10, 0));
        CriarAusencia(db, bruno, criadoEm: Utc(2026, 11, 2, 10, 0));

        var lista = await service.ListarAusenciasAsync(null);

        Assert.Equal(2, lista.Count);
        Assert.Equal("Bruno", lista[0].UsuarioNome);
        Assert.Null(lista[0].FuncionarioId);
        Assert.Equal("Ana", lista[1].UsuarioNome);
        Assert.Equal("ana@empresa.com", lista[1].UsuarioEmail);
        Assert.Equal(ana.Id, lista[1].UsuarioId);
        Assert.Equal(7, lista[1].FuncionarioId);
    }

    [Fact]
    public async Task ListarAusencias_ComStatus_FiltraPorEle()
    {
        var service = CriarServico(out var db);
        var ana = CriarUsuario(db, "Ana");
        CriarAusencia(db, ana, StatusAusencia.Enviada);
        CriarAusencia(db, ana, StatusAusencia.Aprovada);

        var lista = await service.ListarAusenciasAsync(StatusAusencia.Aprovada);

        Assert.Equal(StatusAusencia.Aprovada, Assert.Single(lista).Status);
    }

    [Theory]
    [InlineData(StatusAusencia.Enviada, StatusAusencia.EmAnalise)]
    [InlineData(StatusAusencia.Enviada, StatusAusencia.Aprovada)]
    [InlineData(StatusAusencia.Enviada, StatusAusencia.Recusada)]
    [InlineData(StatusAusencia.EmAnalise, StatusAusencia.Aprovada)]
    [InlineData(StatusAusencia.EmAnalise, StatusAusencia.Recusada)]
    public async Task AtualizarStatus_TransicaoPermitida_Salva(StatusAusencia atual, StatusAusencia novo)
    {
        var service = CriarServico(out var db);
        var ausencia = CriarAusencia(db, CriarUsuario(db, "Ana"), atual);

        var resultado = await service.AtualizarStatusAusenciaAsync(ausencia.Id, new AtualizarStatusAusenciaDto { Status = novo });

        Assert.Equal(novo, resultado.Status);
        Assert.Equal("Ana", resultado.UsuarioNome);
        Assert.Equal(novo, db.Ausencias.Single().Status);
    }

    [Theory]
    [InlineData(StatusAusencia.Aprovada, StatusAusencia.Recusada)]
    [InlineData(StatusAusencia.Recusada, StatusAusencia.Aprovada)]
    [InlineData(StatusAusencia.Aprovada, StatusAusencia.EmAnalise)]
    public async Task AtualizarStatus_JustificativaJaConcluida_LancaBusinessRuleException(StatusAusencia atual, StatusAusencia novo)
    {
        var service = CriarServico(out var db);
        var ausencia = CriarAusencia(db, CriarUsuario(db, "Ana"), atual);

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AtualizarStatusAusenciaAsync(ausencia.Id, new AtualizarStatusAusenciaDto { Status = novo }));

        Assert.Equal("Essa justificativa já foi concluída e não pode mais ser alterada.", erro.Message);
        Assert.Equal(atual, db.Ausencias.Single().Status);
    }

    [Theory]
    [InlineData(StatusAusencia.Enviada)]
    [InlineData((StatusAusencia)99)]
    [InlineData((StatusAusencia)(-1))]
    public async Task AtualizarStatus_ComStatusInvalido_LancaBusinessRuleException(StatusAusencia novo)
    {
        var service = CriarServico(out var db);
        var ausencia = CriarAusencia(db, CriarUsuario(db, "Ana"), StatusAusencia.EmAnalise);

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AtualizarStatusAusenciaAsync(ausencia.Id, new AtualizarStatusAusenciaDto { Status = novo }));

        Assert.Equal("Status inválido.", erro.Message);
    }

    [Fact]
    public async Task AtualizarStatus_JustificativaInexistente_LancaNotFoundException()
    {
        var service = CriarServico(out _);

        var erro = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AtualizarStatusAusenciaAsync(999, new AtualizarStatusAusenciaDto { Status = StatusAusencia.Aprovada }));

        Assert.Equal("Justificativa de ausência não encontrada.", erro.Message);
    }

    // ── Ponto ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarPonto_ComData_UsaOsLimitesDoDiaEmBrasilia()
    {
        var service = CriarServico(out var db);
        var ana = CriarUsuario(db, "Ana");
        db.RegistrosPonto.AddRange(
            new RegistroPonto { UsuarioId = ana.Id, Tipo = TipoRegistroPonto.Saida, RegistradoEm = Utc(2026, 11, 13, 2, 59) },     // 12/11 23:59 em Brasília
            new RegistroPonto { UsuarioId = ana.Id, Tipo = TipoRegistroPonto.Entrada, RegistradoEm = Utc(2026, 11, 13, 3, 0) },    // 13/11 00:00
            new RegistroPonto { UsuarioId = ana.Id, Tipo = TipoRegistroPonto.Saida, RegistradoEm = Utc(2026, 11, 14, 2, 59) },     // 13/11 23:59
            new RegistroPonto { UsuarioId = ana.Id, Tipo = TipoRegistroPonto.Entrada, RegistradoEm = Utc(2026, 11, 14, 3, 0) });   // 14/11 00:00
        await db.SaveChangesAsync();

        var registros = await service.ListarPontoDoDiaAsync("2026-11-13");

        Assert.Equal(new[] { Utc(2026, 11, 13, 3, 0), Utc(2026, 11, 14, 2, 59) }, registros.Select(r => r.RegistradoEm));
        Assert.All(registros, r => Assert.Equal(DateTimeKind.Utc, r.RegistradoEm.Kind));
    }

    [Fact]
    public async Task ListarPonto_OrdenaPorNomeEDepoisPorHorario()
    {
        var service = CriarServico(out var db);
        var bruno = CriarUsuario(db, "Bruno");
        var ana = CriarUsuario(db, "Ana");
        db.RegistrosPonto.AddRange(
            new RegistroPonto { UsuarioId = bruno.Id, Tipo = TipoRegistroPonto.Entrada, RegistradoEm = Utc(2026, 11, 13, 11, 0) },
            new RegistroPonto { UsuarioId = ana.Id, Tipo = TipoRegistroPonto.SaidaAlmoco, RegistradoEm = Utc(2026, 11, 13, 15, 0) },
            new RegistroPonto { UsuarioId = ana.Id, Tipo = TipoRegistroPonto.Entrada, RegistradoEm = Utc(2026, 11, 13, 12, 0) });
        await db.SaveChangesAsync();

        var registros = await service.ListarPontoDoDiaAsync("2026-11-13");

        Assert.Equal(new[] { "Ana", "Ana", "Bruno" }, registros.Select(r => r.UsuarioNome));
        Assert.Equal(TipoRegistroPonto.Entrada, registros[0].Tipo);
        Assert.Equal(TipoRegistroPonto.SaidaAlmoco, registros[1].Tipo);
        Assert.Equal("ana@empresa.com", registros[0].UsuarioEmail);
        Assert.Equal(ana.Id, registros[0].UsuarioId);
    }

    [Fact]
    public async Task ListarPonto_SemData_UsaHojeEmBrasilia()
    {
        var service = CriarServico(out var db);
        var ana = CriarUsuario(db, "Ana");
        var (inicioDeHoje, _) = HorarioBrasilia.LimitesDoDiaEmUtc(HorarioBrasilia.Hoje());
        db.RegistrosPonto.AddRange(
            new RegistroPonto { UsuarioId = ana.Id, Tipo = TipoRegistroPonto.Entrada, RegistradoEm = inicioDeHoje.AddMinutes(1) },
            new RegistroPonto { UsuarioId = ana.Id, Tipo = TipoRegistroPonto.Saida, RegistradoEm = inicioDeHoje.AddMinutes(-1) });
        await db.SaveChangesAsync();

        var registros = await service.ListarPontoDoDiaAsync(null);

        Assert.Equal(TipoRegistroPonto.Entrada, Assert.Single(registros).Tipo);
    }

    [Theory]
    [InlineData("13/11/2026")]
    [InlineData("2026-13-01")]
    [InlineData("2026-11-13T10:00")]
    [InlineData("hoje")]
    public async Task ListarPonto_ComDataInvalida_LancaBusinessRuleException(string data)
    {
        var service = CriarServico(out _);

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ListarPontoDoDiaAsync(data));

        Assert.Equal("Data inválida. Use o formato AAAA-MM-DD.", erro.Message);
    }

    // ── HorarioBrasilia ──────────────────────────────────────────────────

    [Theory]
    [InlineData(2, 59, 13)]  // 23:59 do dia 13 em Brasília
    [InlineData(3, 0, 14)]   // meia-noite do dia 14 em Brasília
    public void Hoje_ConsideraOFusoDeBrasilia(int horaUtc, int minutoUtc, int diaEsperado)
    {
        var hoje = HorarioBrasilia.Hoje(Utc(2026, 11, 14, horaUtc, minutoUtc));

        Assert.Equal(new DateOnly(2026, 11, diaEsperado), hoje);
    }

    [Fact]
    public void LimitesDoDiaEmUtc_VaiDas3hUtcAte3hUtcDoDiaSeguinte()
    {
        var (inicio, fim) = HorarioBrasilia.LimitesDoDiaEmUtc(new DateOnly(2026, 11, 13));

        Assert.Equal(Utc(2026, 11, 13, 3, 0), inicio);
        Assert.Equal(Utc(2026, 11, 14, 3, 0), fim);
        Assert.Equal(DateTimeKind.Utc, inicio.Kind);
    }
}
