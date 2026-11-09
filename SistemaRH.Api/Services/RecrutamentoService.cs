using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IRecrutamentoService
{
    Task<List<RecrutamentoDto>> ListarAsync(StatusRecrutamento? status);
    Task<RecrutamentoDto> CriarAsync(RecrutamentoCreateDto dto);
    Task<RecrutamentoDto> AtualizarStatusAsync(int id, AtualizarStatusRecrutamentoDto dto);
}

/// <summary>Acompanhamento de candidatos num processo seletivo.</summary>
public class RecrutamentoService : IRecrutamentoService
{
    private readonly AppDbContext _db;

    public RecrutamentoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<RecrutamentoDto>> ListarAsync(StatusRecrutamento? status)
    {
        var query = _db.Recrutamentos.AsQueryable();
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        var candidatos = await query.OrderByDescending(r => r.DataCandidatura).ToListAsync();
        return candidatos.Select(ParaDto).ToList();
    }

    public async Task<RecrutamentoDto> CriarAsync(RecrutamentoCreateDto dto)
    {
        var recrutamento = new Recrutamento
        {
            NomeCandidato = dto.NomeCandidato.Trim(),
            Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(),
            Telefone = string.IsNullOrWhiteSpace(dto.Telefone) ? null : dto.Telefone.Trim(),
            Vaga = dto.Vaga.Trim(),
            DataCandidatura = dto.DataCandidatura,
            Status = StatusRecrutamento.EmProcesso,
            Observacoes = dto.Observacoes.Trim(),
            CriadoEm = DateTime.UtcNow,
        };

        _db.Recrutamentos.Add(recrutamento);
        await _db.SaveChangesAsync();

        return ParaDto(recrutamento);
    }

    public async Task<RecrutamentoDto> AtualizarStatusAsync(int id, AtualizarStatusRecrutamentoDto dto)
    {
        var recrutamento = await _db.Recrutamentos.FindAsync(id);
        if (recrutamento is null)
        {
            throw new NotFoundException("Candidato não encontrado.");
        }

        recrutamento.Status = dto.Status;
        if (dto.Observacoes is not null)
        {
            recrutamento.Observacoes = dto.Observacoes.Trim();
        }

        await _db.SaveChangesAsync();
        return ParaDto(recrutamento);
    }

    private static RecrutamentoDto ParaDto(Recrutamento recrutamento) => new()
    {
        Id = recrutamento.Id,
        NomeCandidato = recrutamento.NomeCandidato,
        Email = recrutamento.Email,
        Telefone = recrutamento.Telefone,
        Vaga = recrutamento.Vaga,
        DataCandidatura = recrutamento.DataCandidatura,
        Status = recrutamento.Status,
        Observacoes = recrutamento.Observacoes,
    };
}
