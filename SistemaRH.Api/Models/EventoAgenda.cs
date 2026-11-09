namespace SistemaRH.Api.Models;

public enum TipoEventoAgenda
{
    Reuniao = 0,
    Entrevista = 1,
    Avaliacao = 2,
    Outro = 3,
}

/// <summary>
/// Um compromisso na agenda do RH (reunião, entrevista, avaliação...).
/// Pode estar ligado a um funcionário específico, ou não (ex.: uma reunião
/// geral da equipe).
/// </summary>
public class EventoAgenda
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public DateTime DataEvento { get; set; }
    public TipoEventoAgenda Tipo { get; set; } = TipoEventoAgenda.Outro;

    public int? FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }
}
