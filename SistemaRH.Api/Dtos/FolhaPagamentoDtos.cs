using System.ComponentModel.DataAnnotations;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra lançar o holerite de um funcionário num mês específico.</summary>
public class FolhaPagamentoCreateDto
{
    /// <summary>Mês de referência, no formato "AAAA-MM" (ex.: "2026-08").</summary>
    [Required(ErrorMessage = "Informe a competência (AAAA-MM).")]
    [RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$", ErrorMessage = "A competência precisa estar no formato AAAA-MM (ex.: 2026-08).")]
    public string Competencia { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "O salário não pode ser negativo.")]
    public decimal Salario { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "O bônus não pode ser negativo.")]
    public decimal Bonus { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Os descontos não podem ser negativos.")]
    public decimal Descontos { get; set; }

    public DateTime? DataPagamento { get; set; }
}

/// <summary>O que a API devolve ao listar ou consultar uma folha de pagamento.</summary>
public class FolhaPagamentoDto
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public string Competencia { get; set; } = string.Empty;
    public decimal Salario { get; set; }
    public decimal Bonus { get; set; }
    public decimal Descontos { get; set; }
    public decimal SalarioLiquido { get; set; }
    public DateTime? DataPagamento { get; set; }
}
