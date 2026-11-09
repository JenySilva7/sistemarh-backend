namespace SistemaRH.Api.Models;

public enum TipoLancamentoBancoHoras
{
    Credito = 0,
    Debito = 1,
}

/// <summary>
/// Um lançamento no extrato do banco de horas de um funcionário (ex.: hora
/// extra feita, ou hora compensada depois). O saldo nunca fica guardado num
/// campo à parte — é sempre a SOMA de créditos menos débitos, calculada na
/// hora, o que deixa o histórico auditável (dá pra ver de onde cada hora veio).
/// </summary>
public class LancamentoBancoHoras
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }
    public DateTime DataRegistro { get; set; }
    public TipoLancamentoBancoHoras Tipo { get; set; }
    public int QuantidadeMinutos { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
