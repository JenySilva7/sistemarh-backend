using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface ILancamentoBancoHorasService
{
    Task<LancamentoBancoHorasDto> RegistrarAsync(int funcionarioId, LancamentoBancoHorasCreateDto dto);
    Task<ExtratoBancoHorasDto> ObterExtratoAsync(int funcionarioId);
}

/// <summary>
/// Extrato do banco de horas de um funcionário. O saldo nunca é guardado —
/// é sempre recalculado somando os créditos e subtraindo os débitos (ver
/// <see cref="CalcularSaldoAsync"/>, também reaproveitado pelo AuthService
/// pra unificar com o saldo "achatado" do app).
/// </summary>
public class LancamentoBancoHorasService : ILancamentoBancoHorasService
{
    private readonly AppDbContext _db;

    public LancamentoBancoHorasService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<LancamentoBancoHorasDto> RegistrarAsync(int funcionarioId, LancamentoBancoHorasCreateDto dto)
    {
        var funcionarioExiste = await _db.Funcionarios.AnyAsync(f => f.Id == funcionarioId);
        if (!funcionarioExiste)
        {
            throw new NotFoundException("Funcionário não encontrado.");
        }

        var lancamento = new LancamentoBancoHoras
        {
            FuncionarioId = funcionarioId,
            DataRegistro = dto.DataRegistro,
            Tipo = dto.Tipo,
            QuantidadeMinutos = dto.QuantidadeMinutos,
            Descricao = dto.Descricao.Trim(),
            CriadoEm = DateTime.UtcNow,
        };

        _db.LancamentosBancoHoras.Add(lancamento);
        await _db.SaveChangesAsync();

        return ParaDto(lancamento);
    }

    public async Task<ExtratoBancoHorasDto> ObterExtratoAsync(int funcionarioId)
    {
        var funcionarioExiste = await _db.Funcionarios.AnyAsync(f => f.Id == funcionarioId);
        if (!funcionarioExiste)
        {
            throw new NotFoundException("Funcionário não encontrado.");
        }

        var lancamentos = await _db.LancamentosBancoHoras
            .Where(l => l.FuncionarioId == funcionarioId)
            .OrderByDescending(l => l.DataRegistro)
            .ToListAsync();

        return new ExtratoBancoHorasDto
        {
            SaldoMinutos = SomarSaldo(lancamentos),
            Lancamentos = lancamentos.Select(ParaDto).ToList(),
        };
    }

    /// <summary>
    /// Soma o saldo (créditos - débitos) de um funcionário direto no banco,
    /// sem carregar os lançamentos pra memória — usado pelo AuthService pra
    /// unificar o saldo do app com esse extrato.
    /// </summary>
    public static async Task<int> CalcularSaldoAsync(AppDbContext db, int funcionarioId)
    {
        var creditos = await db.LancamentosBancoHoras
            .Where(l => l.FuncionarioId == funcionarioId && l.Tipo == TipoLancamentoBancoHoras.Credito)
            .SumAsync(l => l.QuantidadeMinutos);

        var debitos = await db.LancamentosBancoHoras
            .Where(l => l.FuncionarioId == funcionarioId && l.Tipo == TipoLancamentoBancoHoras.Debito)
            .SumAsync(l => l.QuantidadeMinutos);

        return creditos - debitos;
    }

    private static int SomarSaldo(List<LancamentoBancoHoras> lancamentos) =>
        lancamentos.Sum(l => l.Tipo == TipoLancamentoBancoHoras.Credito ? l.QuantidadeMinutos : -l.QuantidadeMinutos);

    private static LancamentoBancoHorasDto ParaDto(LancamentoBancoHoras lancamento) => new()
    {
        Id = lancamento.Id,
        DataRegistro = lancamento.DataRegistro,
        Tipo = lancamento.Tipo,
        QuantidadeMinutos = lancamento.QuantidadeMinutos,
        Descricao = lancamento.Descricao,
        CriadoEm = lancamento.CriadoEm,
    };
}
