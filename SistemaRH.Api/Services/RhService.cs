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
    /// <summary>Primeiro dia aceito em GET /api/rh/ponto (antes disso não há ponto no sistema).</summary>
    public static readonly DateOnly PrimeiroDiaDoPonto = new(2000, 1, 1);

    private readonly AppDbContext _db;

    public RhService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<AusenciaRhDto>> ListarAusenciasAsync(StatusAusencia? status)
    {
        Enums.ValidarFiltro(status, "Status inválido.");

        var query = _db.Ausencias.Include(a => a.Usuario).AsQueryable();
        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }

        var ausencias = await query.OrderByDescending(a => a.CriadoEm).ThenByDescending(a => a.Id).ToListAsync();
        return ausencias.Select(ParaDto).ToList();
    }

    public async Task<AusenciaRhDto> AtualizarStatusAusenciaAsync(int ausenciaId, AtualizarStatusAusenciaDto dto)
    {
        // "Enviada" é só o status inicial — o RH não devolve uma justificativa pra ele.
        var statusPermitido = dto.Status is StatusAusencia novo && Enum.IsDefined(novo) && novo != StatusAusencia.Enviada;
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

        // A decisão só vale sobre o que o RH viu: se o colaborador editou ou
        // outra aba já decidiu depois que a tela carregou, 409. E mesmo sem a
        // versão na requisição, o UPDATE confere a versão lida aqui — duas
        // decisões ao mesmo tempo não dão mais "sucesso" para as duas.
        Concorrencia.ConferirVersao(ausencia, dto.Versao);
        if (ausencia.Status is not (StatusAusencia.Enviada or StatusAusencia.EmAnalise))
        {
            throw new BusinessRuleException("Essa justificativa já foi concluída e não pode mais ser alterada.");
        }

        ausencia.Status = dto.Status!.Value;
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

        // Uma data como 9999-12-31 estourava o DateTime ao calcular o fim do
        // dia (500). Fora dessa faixa não existe ponto, então é 400.
        var ultimoDia = HorarioBrasilia.Hoje().AddDays(366);
        if (dia < PrimeiroDiaDoPonto || dia > ultimoDia)
        {
            var formato = CultureInfo.InvariantCulture;
            throw new BusinessRuleException(
                $"Data fora do período permitido: escolha um dia entre {PrimeiroDiaDoPonto.ToString("dd/MM/yyyy", formato)} e {ultimoDia.ToString("dd/MM/yyyy", formato)}.");
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
        Versao = ausencia.Versao,
        UsuarioId = ausencia.UsuarioId,
        UsuarioNome = ausencia.Usuario?.Nome ?? string.Empty,
        UsuarioEmail = ausencia.Usuario?.Email ?? string.Empty,
        FuncionarioId = ausencia.Usuario?.FuncionarioId,
    };
}
