using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SistemaRH.Api.Common;

namespace SistemaRH.Api.Dtos;

/// <summary>
/// Dados pra lançar o holerite de um funcionário num mês específico. Faixas
/// (validadas no FolhaPagamentoService): valores de R$ 0,00 a R$ 1.000.000,00
/// com 2 casas, líquido nunca negativo e competência entre o mês da admissão
/// e o mês que vem.
/// </summary>
public class FolhaPagamentoCreateDto
{
    /// <summary>Mês de referência, no formato "AAAA-MM" (ex.: "2026-08").</summary>
    [Required(ErrorMessage = "Informe a competência (AAAA-MM).")]
    [RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$", ErrorMessage = "A competência precisa estar no formato AAAA-MM (ex.: 2026-08).")]
    public string Competencia { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o salário da folha.")]
    public decimal? Salario { get; set; }

    public decimal? Bonus { get; set; }

    public decimal? Descontos { get; set; }

    [JsonConverter(typeof(DataCalendarioJsonConverter))]
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
