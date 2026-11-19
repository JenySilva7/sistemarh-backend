using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SistemaRH.Api.Common;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra criar um compromisso na agenda do RH.</summary>
public class EventoAgendaCreateDto
{
    public string? Titulo { get; set; }

    public string? Descricao { get; set; }

    // Anulável: sem isso, um evento sem data era gravado em 01/01/0001.
    // Hora de Brasília, sem fuso (com fuso, é convertida pra Brasília).
    [Required(ErrorMessage = "Informe a data/hora do evento.")]
    [JsonConverter(typeof(DataHoraBrasiliaJsonConverter))]
    public DateTime? DataEvento { get; set; }

    /// <summary>Opcional (padrão: Outro); um número fora da lista é recusado.</summary>
    public TipoEventoAgenda? Tipo { get; set; }

    /// <summary>Opcional — deixe em branco pra um evento que não é sobre um funcionário específico.</summary>
    public int? FuncionarioId { get; set; }
}

/// <summary>O que a API devolve ao listar ou consultar um evento da agenda.</summary>
public class EventoAgendaDto
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public DateTime DataEvento { get; set; }
    public TipoEventoAgenda Tipo { get; set; }
    public int? FuncionarioId { get; set; }
}
