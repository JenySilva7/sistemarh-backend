using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IFolhaPagamentoService
{
    Task<FolhaPagamentoDto> CriarAsync(int funcionarioId, FolhaPagamentoCreateDto dto);
    Task<List<FolhaPagamentoDto>> ListarPorFuncionarioAsync(int funcionarioId);
}

/// <summary>
/// Holerites (folha de pagamento) de um funcionário. O salário líquido
/// nunca é digitado — é sempre calculado aqui (salário + bônus - descontos).
/// Não existe trigger de banco pra isso: essa é a decisão arquitetural do
/// projeto (regra de negócio mora em C#, não no banco).
/// </summary>
public class FolhaPagamentoService : IFolhaPagamentoService
{
    private readonly AppDbContext _db;

    public FolhaPagamentoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<FolhaPagamentoDto> CriarAsync(int funcionarioId, FolhaPagamentoCreateDto dto)
    {
        var funcionarioExiste = await _db.Funcionarios.AnyAsync(f => f.Id == funcionarioId);
        if (!funcionarioExiste)
        {
            throw new NotFoundException("Funcionário não encontrado.");
        }

        var competencia = dto.Competencia.Trim();
        var jaExiste = await _db.FolhasPagamento
            .AnyAsync(fp => fp.FuncionarioId == funcionarioId && fp.Competencia == competencia);
        if (jaExiste)
        {
            throw new ConflictException($"Já existe uma folha de pagamento desse funcionário pra competência {competencia}.");
        }

        var folha = new FolhaPagamento
        {
            FuncionarioId = funcionarioId,
            Competencia = competencia,
            Salario = dto.Salario,
            Bonus = dto.Bonus,
            Descontos = dto.Descontos,
            SalarioLiquido = dto.Salario + dto.Bonus - dto.Descontos,
            DataPagamento = dto.DataPagamento,
        };

        _db.FolhasPagamento.Add(folha);
        await _db.SaveChangesAsync();

        return ParaDto(folha);
    }

    public async Task<List<FolhaPagamentoDto>> ListarPorFuncionarioAsync(int funcionarioId)
    {
        var folhas = await _db.FolhasPagamento
            .Where(fp => fp.FuncionarioId == funcionarioId)
            .OrderByDescending(fp => fp.Competencia)
            .ToListAsync();

        return folhas.Select(ParaDto).ToList();
    }

    private static FolhaPagamentoDto ParaDto(FolhaPagamento folha) => new()
    {
        Id = folha.Id,
        FuncionarioId = folha.FuncionarioId,
        Competencia = folha.Competencia,
        Salario = folha.Salario,
        Bonus = folha.Bonus,
        Descontos = folha.Descontos,
        SalarioLiquido = folha.SalarioLiquido,
        DataPagamento = folha.DataPagamento,
    };
}
