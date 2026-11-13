using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IRhService
{
    Task<List<AusenciaRhDto>> ListarAusenciasAsync(StatusAusencia? status);
    Task<AusenciaRhDto> AtualizarStatusAusenciaAsync(int ausenciaId, AtualizarStatusAusenciaDto dto);
    Task<List<RegistroPontoRhDto>> ListarPontoDoDiaAsync(string? data);
}

/// <summary>
/// Visão do RH sobre o que os colaboradores enviam pelo app: justificativas
/// de ausência de todo mundo (com aprovação/recusa) e as marcações de ponto
/// de um dia. Diferente de <see cref="AusenciaService"/> e
/// <see cref="PontoService"/>, que só enxergam os dados do próprio usuário.
/// </summary>
public class RhService : IRhService
{
    private readonly AppDbContext _db;

    public RhService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<AusenciaRhDto>> ListarAusenciasAsync(StatusAusencia? status)
    {
        var query = _db.Ausencias.Include(a => a.Usuario).AsQueryable();
        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }

        var ausencias = await query.OrderByDescending(a => a.CriadoEm).ToListAsync();
        return ausencias.Select(ParaDto).ToList();
    }

    public async Task<AusenciaRhDto> AtualizarStatusAusenciaAsync(int ausenciaId, AtualizarStatusAusenciaDto dto)
    {
        // "Enviada" é só o status inicial — o RH não devolve uma justificativa pra ele.
        var statusPermitido = Enum.IsDefined(dto.Status) && dto.Status != StatusAusencia.Enviada;
        if (!statusPermitido)
        {
            throw new BusinessRuleException("Status inválido.");
        }

        var ausencia = await _db.Ausencias
            .Include(a => a.Usuario)
            .FirstOrDefaultAsync(a => a.Id == ausenciaId);
        if (ausencia is null)
        {
            throw new NotFoundException("Justificativa de ausência não encontrada.");
        }

        if (ausencia.Status is StatusAusencia.Aprovada or StatusAusencia.Recusada)
        {
            throw new BusinessRuleException("Essa justificativa já foi concluída e não pode mais ser alterada.");
        }

        ausencia.Status = dto.Status;
        await _db.SaveChangesAsync();

        return ParaDto(ausencia);
    }

    public async Task<List<RegistroPontoRhDto>> ListarPontoDoDiaAsync(string? data)
    {
        DateOnly dia;
        if (string.IsNullOrWhiteSpace(data))
        {
            dia = HorarioBrasilia.Hoje();
        }
        else if (!DateOnly.TryParseExact(data.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dia))
        {
            throw new BusinessRuleException("Data inválida. Use o formato AAAA-MM-DD.");
        }

        var (inicioDoDia, fimDoDia) = HorarioBrasilia.LimitesDoDiaEmUtc(dia);

        // UsuarioId no desempate pra não misturar as marcações de duas
        // pessoas com o mesmo nome.
        var registros = await _db.RegistrosPonto
            .Include(r => r.Usuario)
            .Where(r => r.RegistradoEm >= inicioDoDia && r.RegistradoEm < fimDoDia)
            .OrderBy(r => r.Usuario!.Nome)
            .ThenBy(r => r.UsuarioId)
            .ThenBy(r => r.RegistradoEm)
            .ToListAsync();

        return registros.Select(r => new RegistroPontoRhDto
        {
            Id = r.Id,
            Tipo = r.Tipo,
            RegistradoEm = r.RegistradoEm,
            UsuarioId = r.UsuarioId,
            UsuarioNome = r.Usuario?.Nome ?? string.Empty,
            UsuarioEmail = r.Usuario?.Email ?? string.Empty,
        }).ToList();
    }

    private static AusenciaRhDto ParaDto(Ausencia ausencia) => new()
    {
        Id = ausencia.Id,
        Tipo = ausencia.Tipo,
        Data = ausencia.Data,
        DiaInteiro = ausencia.DiaInteiro,
        Descricao = ausencia.Descricao,
        Status = ausencia.Status,
        CriadoEm = ausencia.CriadoEm,
        UsuarioId = ausencia.UsuarioId,
        UsuarioNome = ausencia.Usuario?.Nome ?? string.Empty,
        UsuarioEmail = ausencia.Usuario?.Email ?? string.Empty,
        FuncionarioId = ausencia.Usuario?.FuncionarioId,
    };
}
