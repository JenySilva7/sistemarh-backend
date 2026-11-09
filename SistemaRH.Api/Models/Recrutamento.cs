namespace SistemaRH.Api.Models;

public enum StatusRecrutamento
{
    EmProcesso = 0,
    Entrevista = 1,
    Aprovado = 2,
    Rejeitado = 3,
}

/// <summary>Um candidato a uma vaga, acompanhado durante o processo seletivo.</summary>
public class Recrutamento
{
    public int Id { get; set; }
    public string NomeCandidato { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string Vaga { get; set; } = string.Empty;
    public DateTime DataCandidatura { get; set; }
    public StatusRecrutamento Status { get; set; } = StatusRecrutamento.EmProcesso;
    public string Observacoes { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
