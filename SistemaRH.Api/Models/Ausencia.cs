namespace SistemaRH.Api.Models;

public enum TipoAusencia
{
    AtestadoMedico = 0,
    FaltaJustificada = 1,
    EsquecimentoDePonto = 2,
    Outro = 3,
}

/// <summary>
/// Status que acompanha o ciclo de vida de uma justificativa de ausência,
/// igual ao enum AusenciaStatus que existia no app Flutter.
/// </summary>
public enum StatusAusencia
{
    Enviada = 0,
    EmAnalise = 1,
    Aprovada = 2,
    Recusada = 3,
}

/// <summary>
/// Uma justificativa de ausência enviada pelo colaborador pro RH analisar.
/// Só pode ser editada ou cancelada pelo próprio dono, e só enquanto o
/// status ainda for <see cref="StatusAusencia.Enviada"/> (ver AusenciaService).
/// </summary>
public class Ausencia
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public TipoAusencia Tipo { get; set; }
    public DateTime Data { get; set; }
    public bool DiaInteiro { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public StatusAusencia Status { get; set; } = StatusAusencia.Enviada;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
