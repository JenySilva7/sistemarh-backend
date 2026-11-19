using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
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
    private const string MensagemFaixa = "precisa estar entre R$ 0,00 e R$ 1.000.000,00.";

    private readonly AppDbContext _db;

    public FolhaPagamentoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<FolhaPagamentoDto> CriarAsync(int funcionarioId, FolhaPagamentoCreateDto dto)
    {
        var funcionario = await _db.Funcionarios.FirstOrDefaultAsync(f => f.Id == funcionarioId)
            ?? throw new NotFoundException("Funcionário não encontrado.");

        var competencia = ValidarCompetencia(dto.Competencia, funcionario.DataAdmissao);

        // Teto e casas decimais batem com a coluna decimal(10,2): antes um
        // salário de 1 bilhão dava 500 e 0,005 era devolvido assim, mas gravado 0,01.
        var salario = Dinheiro.Validar(dto.Salario, "Informe o salário da folha.", 0m, Dinheiro.FolhaMaximo,
            $"O salário da folha {MensagemFaixa}", "o salário da folha");
        var bonus = Dinheiro.Validar(dto.Bonus ?? 0m, string.Empty, 0m, Dinheiro.FolhaMaximo,
            $"O bônus {MensagemFaixa}", "o bônus");
        var descontos = Dinheiro.Validar(dto.Descontos ?? 0m, string.Empty, 0m, Dinheiro.FolhaMaximo,
            $"O valor dos descontos {MensagemFaixa}", "os descontos");
        var liquido = salario + bonus - descontos;
        if (liquido < 0)
        {
            throw new BusinessRuleException("Os descontos não podem ser maiores que o salário mais o bônus.");
        }

        DateTime? dataPagamento = null;
        if (dto.DataPagamento is not null)
        {
            dataPagamento = Datas.ValidarFaixa(dto.DataPagamento, string.Empty, "A data de pagamento",
                funcionario.DataAdmissao, Datas.Hoje().AddDays(365));
        }

        var mensagemDuplicada = $"Já existe uma folha de pagamento desse funcionário pra competência {competencia}.";
        var jaExiste = await _db.FolhasPagamento
            .AnyAsync(fp => fp.FuncionarioId == funcionarioId && fp.Competencia == competencia);
        if (jaExiste)
        {
            throw new ConflictException(mensagemDuplicada);
        }

        var folha = new FolhaPagamento
        {
            FuncionarioId = funcionarioId,
            Competencia = competencia,
            Salario = salario,
            Bonus = bonus,
            Descontos = descontos,
            SalarioLiquido = liquido,
            DataPagamento = dataPagamento,
        };

        _db.FolhasPagamento.Add(folha);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ErrosBanco.IndiceDuplicado(ex) is not null)
        {
            // Dois lançamentos da mesma competência ao mesmo tempo: o índice
            // único segurou o segundo (antes isso era 500).
            throw new ConflictException(mensagemDuplicada);
        }

        return ParaDto(folha);
    }

    public async Task<List<FolhaPagamentoDto>> ListarPorFuncionarioAsync(int funcionarioId)
    {
        var folhas = await _db.FolhasPagamento
            .Where(fp => fp.FuncionarioId == funcionarioId)
            .OrderByDescending(fp => fp.Competencia)
            .ThenByDescending(fp => fp.Id)
            .ToListAsync();

        return folhas.Select(ParaDto).ToList();
    }

    /// <summary>"AAAA-MM", do mês da admissão até o mês que vem.</summary>
    private static string ValidarCompetencia(string? valor, DateTime admissao)
    {
        var texto = valor?.Trim() ?? string.Empty;
        if (!DateTime.TryParseExact(texto, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var mes))
        {
            throw new BusinessRuleException("A competência precisa estar no formato AAAA-MM (ex.: 2026-08).");
        }

        var primeiro = new DateTime(admissao.Year, admissao.Month, 1);
        var hoje = Datas.Hoje();
        var ultimo = new DateTime(hoje.Year, hoje.Month, 1).AddMonths(1);
        if (mes < primeiro || mes > ultimo)
        {
            var formato = CultureInfo.InvariantCulture;
            throw new BusinessRuleException(
                $"A competência precisa estar entre {primeiro.ToString("MM/yyyy", formato)} (admissão) e {ultimo.ToString("MM/yyyy", formato)}.");
        }

        return mes.ToString("yyyy-MM", CultureInfo.InvariantCulture);
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
