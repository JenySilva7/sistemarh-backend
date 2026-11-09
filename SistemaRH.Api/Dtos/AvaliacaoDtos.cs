using System.ComponentModel.DataAnnotations;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra registrar uma avaliação de desempenho.</summary>
public class AvaliacaoCreateDto
{
    [Range(0, 10, ErrorMessage = "A nota precisa estar entre 0 e 10.")]
    public decimal Nota { get; set; }

    public string Comentario { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a data da avaliação.")]
    public DateTime DataAvaliacao { get; set; }
}

/// <summary>O que a API devolve ao listar ou consultar uma avaliação.</summary>
public class AvaliacaoDto
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public decimal Nota { get; set; }
    public string Comentario { get; set; } = string.Empty;
    public DateTime DataAvaliacao { get; set; }
}
