using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SistemaRH.Api.Common;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra registrar um lançamento (crédito ou débito) no banco de horas.</summary>
public class LancamentoBancoHorasCreateDto
{
    // Anuláveis: data ou tipo esquecidos não podem virar 01/01/0001 / "Crédito".
    [Required(ErrorMessage = "Informe a data do lançamento.")]
    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataRegistro { get; set; }

    [Required(ErrorMessage = "Selecione o tipo (crédito ou débito).")]
    public TipoLancamentoBancoHoras? Tipo { get; set; }

    // Teto pra soma do saldo nunca estourar um int (dois lançamentos de
    // int.MaxValue derrubavam o extrato com 500).
    [Range(1, 100_000, ErrorMessage = "A quantidade de minutos precisa estar entre 1 e 100000.")]
    public int QuantidadeMinutos { get; set; }

    public string? Descricao { get; set; }
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
