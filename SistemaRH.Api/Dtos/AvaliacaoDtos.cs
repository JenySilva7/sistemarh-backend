using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SistemaRH.Api.Common;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra registrar uma avaliação de desempenho.</summary>
public class AvaliacaoCreateDto
{
    // Anuláveis: uma nota ou data esquecida não pode virar 0 / 01/01/0001.
    [Required(ErrorMessage = "Informe a nota da avaliação.")]
    public decimal? Nota { get; set; }

    public string? Comentario { get; set; }

    [Required(ErrorMessage = "Informe a data da avaliação.")]
    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataAvaliacao { get; set; }
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
