using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
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
        var funcionario = await _db.Funcionarios.FirstOrDefaultAsync(f => f.Id == funcionarioId)
            ?? throw new NotFoundException("Funcionário não encontrado.");

        // Nota de 0 a 10 com no máximo 1 casa: a coluna guarda 2 casas e
        // 9,999 era devolvido assim, mas gravado como 10.
        if (dto.Nota is not decimal nota)
        {
            throw new BusinessRuleException("Informe a nota da avaliação.");
        }
        if (nota < 0 || nota > 10)
        {
            throw new BusinessRuleException("A nota precisa estar entre 0 e 10.");
        }
        if (decimal.Round(nota, 1) != nota)
        {
            throw new BusinessRuleException("A nota pode ter no máximo 1 casa decimal.");
        }

        // Uma avaliação registra algo que já aconteceu: entre a admissão e hoje.
        var data = Datas.ValidarFaixa(dto.DataAvaliacao, "Informe a data da avaliação.", "A data da avaliação",
            funcionario.DataAdmissao, Datas.Hoje());
        var comentario = Textos.ValidarOpcional(dto.Comentario, Textos.Comentario);

        var avaliacao = new Avaliacao
        {
            FuncionarioId = funcionarioId,
            Nota = decimal.Round(nota, 1),
            Comentario = comentario,
            DataAvaliacao = data,
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
            .ThenByDescending(a => a.Id)
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
