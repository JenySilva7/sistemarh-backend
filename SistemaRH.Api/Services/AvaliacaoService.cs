using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IAvaliacaoService
{
    Task<AvaliacaoDto> CriarAsync(int funcionarioId, AvaliacaoCreateDto dto);
    Task<List<AvaliacaoDto>> ListarPorFuncionarioAsync(int funcionarioId);
}

/// <summary>Avaliações de desempenho ligadas a um funcionário.</summary>
public class AvaliacaoService : IAvaliacaoService
{
    private readonly AppDbContext _db;

    public AvaliacaoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AvaliacaoDto> CriarAsync(int funcionarioId, AvaliacaoCreateDto dto)
    {
        var funcionarioExiste = await _db.Funcionarios.AnyAsync(f => f.Id == funcionarioId);
        if (!funcionarioExiste)
        {
            throw new NotFoundException("Funcionário não encontrado.");
        }

        var avaliacao = new Avaliacao
        {
            FuncionarioId = funcionarioId,
            Nota = dto.Nota,
            Comentario = dto.Comentario.Trim(),
            DataAvaliacao = dto.DataAvaliacao,
        };

        _db.Avaliacoes.Add(avaliacao);
        await _db.SaveChangesAsync();

        return ParaDto(avaliacao);
    }

    public async Task<List<AvaliacaoDto>> ListarPorFuncionarioAsync(int funcionarioId)
    {
        var avaliacoes = await _db.Avaliacoes
            .Where(a => a.FuncionarioId == funcionarioId)
            .OrderByDescending(a => a.DataAvaliacao)
            .ToListAsync();

        return avaliacoes.Select(ParaDto).ToList();
    }

    private static AvaliacaoDto ParaDto(Avaliacao avaliacao) => new()
    {
        Id = avaliacao.Id,
        FuncionarioId = avaliacao.FuncionarioId,
        Nota = avaliacao.Nota,
        Comentario = avaliacao.Comentario,
        DataAvaliacao = avaliacao.DataAvaliacao,
    };
}
