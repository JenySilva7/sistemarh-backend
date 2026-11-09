namespace SistemaRH.Api.Models;

/// <summary>Uma avaliação de desempenho de um funcionário, numa data específica.</summary>
public class Avaliacao
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }
    public decimal Nota { get; set; }
    public string Comentario { get; set; } = string.Empty;
    public DateTime DataAvaliacao { get; set; }
}
