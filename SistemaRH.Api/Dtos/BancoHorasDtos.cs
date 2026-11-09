using System.ComponentModel.DataAnnotations;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra registrar um lançamento (crédito ou débito) no banco de horas.</summary>
public class LancamentoBancoHorasCreateDto
{
    [Required(ErrorMessage = "Informe a data do lançamento.")]
    public DateTime DataRegistro { get; set; }

    [Required(ErrorMessage = "Selecione o tipo (crédito ou débito).")]
    public TipoLancamentoBancoHoras Tipo { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "A quantidade de minutos precisa ser maior que zero.")]
    public int QuantidadeMinutos { get; set; }

    public string Descricao { get; set; } = string.Empty;
}

/// <summary>Um lançamento, do jeito que a API devolve.</summary>
public class LancamentoBancoHorasDto
{
    public int Id { get; set; }
    public DateTime DataRegistro { get; set; }
    public TipoLancamentoBancoHoras Tipo { get; set; }
    public int QuantidadeMinutos { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
}

/// <summary>
/// O extrato completo de um funcionário: o saldo atual (soma de créditos
/// menos débitos) e a lista de lançamentos que chegaram a esse número.
/// </summary>
public class ExtratoBancoHorasDto
{
    public int SaldoMinutos { get; set; }
    public List<LancamentoBancoHorasDto> Lancamentos { get; set; } = new();
}
