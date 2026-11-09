using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IAusenciaService
{
    Task<List<AusenciaDto>> ListarAsync(int usuarioId);
    Task<AusenciaDto> ObterPorIdAsync(int usuarioId, int ausenciaId);
    Task<AusenciaDto> CriarAsync(int usuarioId, AusenciaCreateDto dto);
    Task<AusenciaDto> AtualizarAsync(int usuarioId, int ausenciaId, AusenciaCreateDto dto);
    Task CancelarAsync(int usuarioId, int ausenciaId);
}

/// <summary>
/// CRUD de justificativas de ausência. Toda operação de leitura/escrita é
/// restrita ao dono do registro — mesmo que alguém descubra o Id de uma
/// ausência de outra pessoa, não consegue ver nem alterar (ver
/// <see cref="BuscarDoUsuarioOuFalhar"/>).
///
/// Edição e cancelamento só são permitidos enquanto o status ainda é
/// "Enviada": depois que o RH começa a analisar, a solicitação já não é mais
/// alterável pelo colaborador (regra de negócio real de qualquer fluxo de
/// aprovação).
/// </summary>
public class AusenciaService : IAusenciaService
{
    private readonly AppDbContext _db;

    public AusenciaService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<AusenciaDto>> ListarAsync(int usuarioId)
    {
        var ausencias = await _db.Ausencias
            .Where(a => a.UsuarioId == usuarioId)
            .OrderByDescending(a => a.CriadoEm)
            .ToListAsync();

        return ausencias.Select(ParaDto).ToList();
    }

    public async Task<AusenciaDto> ObterPorIdAsync(int usuarioId, int ausenciaId)
    {
        var ausencia = await BuscarDoUsuarioOuFalhar(usuarioId, ausenciaId);
        return ParaDto(ausencia);
    }

    public async Task<AusenciaDto> CriarAsync(int usuarioId, AusenciaCreateDto dto)
    {
        var ausencia = new Ausencia
        {
            UsuarioId = usuarioId,
            Tipo = dto.Tipo,
            Data = dto.Data,
            DiaInteiro = dto.DiaInteiro,
            Descricao = dto.Descricao.Trim(),
            Status = StatusAusencia.Enviada,
            CriadoEm = DateTime.UtcNow,
        };

        _db.Ausencias.Add(ausencia);
        await _db.SaveChangesAsync();

        return ParaDto(ausencia);
    }

    public async Task<AusenciaDto> AtualizarAsync(int usuarioId, int ausenciaId, AusenciaCreateDto dto)
    {
        var ausencia = await BuscarDoUsuarioOuFalhar(usuarioId, ausenciaId);

        if (ausencia.Status != StatusAusencia.Enviada)
        {
            throw new BusinessRuleException(
                "Essa justificativa já está em análise (ou foi concluída) e não pode mais ser editada.");
        }

        ausencia.Tipo = dto.Tipo;
        ausencia.Data = dto.Data;
        ausencia.DiaInteiro = dto.DiaInteiro;
        ausencia.Descricao = dto.Descricao.Trim();

        await _db.SaveChangesAsync();
        return ParaDto(ausencia);
    }

    public async Task CancelarAsync(int usuarioId, int ausenciaId)
    {
        var ausencia = await BuscarDoUsuarioOuFalhar(usuarioId, ausenciaId);

        if (ausencia.Status != StatusAusencia.Enviada)
        {
            throw new BusinessRuleException(
                "Essa justificativa já está em análise (ou foi concluída) e não pode mais ser cancelada.");
        }

        _db.Ausencias.Remove(ausencia);
        await _db.SaveChangesAsync();
    }

    private async Task<Ausencia> BuscarDoUsuarioOuFalhar(int usuarioId, int ausenciaId)
    {
        var ausencia = await _db.Ausencias.FindAsync(ausenciaId);
        if (ausencia is null)
        {
            throw new NotFoundException("Justificativa de ausência não encontrada.");
        }

        if (ausencia.UsuarioId != usuarioId)
        {
            // Não dizemos "essa ausência não é sua" de um jeito que confirme
            // que o Id existe — simplesmente negamos acesso.
            throw new ForbiddenException("Você não tem permissão para acessar essa justificativa.");
        }

        return ausencia;
    }

    private static AusenciaDto ParaDto(Ausencia ausencia) => new()
    {
        Id = ausencia.Id,
        Tipo = ausencia.Tipo,
        Data = ausencia.Data,
        DiaInteiro = ausencia.DiaInteiro,
        Descricao = ausencia.Descricao,
        Status = ausencia.Status,
        CriadoEm = ausencia.CriadoEm,
    };
}
