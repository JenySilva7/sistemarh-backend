using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IPontoService
{
    Task<List<RegistroPontoDto>> ListarRegistrosDeHojeAsync(int usuarioId);
    Task<RegistrarPontoResponseDto> RegistrarProximoAsync(int usuarioId);
}

/// <summary>
/// Regras do ponto: sempre existem 4 marcações por dia, nesta ordem —
/// Entrada, Saída (almoço), Volta (almoço), Saída — igual ao
/// PontoController.dart do app. Cada chamada de "bater o ponto" registra
/// automaticamente a PRÓXIMA marcação pendente do dia; o cliente não escolhe
/// qual tipo registrar, só pede "registre o meu próximo ponto".
/// </summary>
public class PontoService : IPontoService
{
    /// <summary>
    /// Intervalo mínimo entre duas marcações do mesmo usuário. Sem ele, 4
    /// toques seguidos fechavam o dia inteiro de ponto em 70 ms.
    /// </summary>
    public static readonly TimeSpan IntervaloMinimo = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _db;
    private readonly TimeProvider _relogio;

    public PontoService(AppDbContext db, TimeProvider? relogio = null)
    {
        _db = db;
        _relogio = relogio ?? TimeProvider.System;
    }

    public async Task<List<RegistroPontoDto>> ListarRegistrosDeHojeAsync(int usuarioId)
    {
        var registros = await RegistrosDoDiaQuery(usuarioId, AgoraUtc()).ToListAsync();
        return registros.Select(ParaDto).ToList();
    }

    public async Task<RegistrarPontoResponseDto> RegistrarProximoAsync(int usuarioId)
    {
        // Trava a conta do usuário até gravar: 10 envios simultâneos passam a
        // ser atendidos um de cada vez, e cada um enxerga o anterior (antes
        // todos liam "nenhuma marcação" e gravavam 10 Entradas).
        await using var transacao = await _db.IniciarTransacaoAsync();
        await _db.TravarUsuarioAsync(usuarioId);

        var agora = AgoraUtc();
        var registrosHoje = await RegistrosDoDiaQuery(usuarioId, agora).ToListAsync();
        var tiposJaRegistrados = registrosHoje.Select(r => r.Tipo).ToHashSet();

        // Percorre os 4 tipos na ordem do enum e pega o primeiro que ainda
        // não foi batido hoje — esse é o "próximo" a ser registrado agora.
        var proximoTipo = Enum.GetValues<TipoRegistroPonto>()
            .Cast<TipoRegistroPonto>()
            .FirstOrDefault(tipo => !tiposJaRegistrados.Contains(tipo), (TipoRegistroPonto)(-1));

        if ((int)proximoTipo == -1)
        {
            throw new BusinessRuleException("Todas as marcações de ponto de hoje já foram registradas.");
        }

        var ultimaMarcacao = await _db.RegistrosPonto
            .Where(r => r.UsuarioId == usuarioId)
            .OrderByDescending(r => r.RegistradoEm)
            .Select(r => (DateTime?)r.RegistradoEm)
            .FirstOrDefaultAsync();
        if (ultimaMarcacao is DateTime ultima && agora - ultima < IntervaloMinimo)
        {
            throw new BusinessRuleException("Aguarde 1 minuto entre registros de ponto.");
        }

        var registro = new RegistroPonto
        {
            UsuarioId = usuarioId,
            Tipo = proximoTipo,
            RegistradoEm = agora,
        };
        _db.RegistrosPonto.Add(registro);
        await _db.SaveChangesAsync();
        await AppDbContext.ConfirmarAsync(transacao);

        var aindaFaltam = tiposJaRegistrados.Count + 1 < 4;
        TipoRegistroPonto? proximoDepoisDesse = null;
        if (aindaFaltam)
        {
            tiposJaRegistrados.Add(proximoTipo);
            proximoDepoisDesse = Enum.GetValues<TipoRegistroPonto>()
                .Cast<TipoRegistroPonto>()
                .First(tipo => !tiposJaRegistrados.Contains(tipo));
        }

        return new RegistrarPontoResponseDto
        {
            Registro = ParaDto(registro),
            Proximo = proximoDepoisDesse,
        };
    }

    private DateTime AgoraUtc() => _relogio.GetUtcNow().UtcDateTime;

    private IQueryable<RegistroPonto> RegistrosDoDiaQuery(int usuarioId, DateTime agoraUtc)
    {
        // "Hoje" é o dia de Brasília, não o dia UTC (ver HorarioBrasilia).
        var (inicioDoDia, fimDoDia) = HorarioBrasilia.LimitesDoDiaEmUtc(HorarioBrasilia.Hoje(agoraUtc));

        return _db.RegistrosPonto
            .Where(r => r.UsuarioId == usuarioId && r.RegistradoEm >= inicioDoDia && r.RegistradoEm < fimDoDia)
            .OrderBy(r => r.RegistradoEm);
    }

    private static RegistroPontoDto ParaDto(RegistroPonto registro) => new()
    {
        Id = registro.Id,
        Tipo = registro.Tipo,
        RegistradoEm = registro.RegistradoEm,
    };
}
