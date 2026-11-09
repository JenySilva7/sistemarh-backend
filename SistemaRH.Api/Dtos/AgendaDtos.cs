using System.ComponentModel.DataAnnotations;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra criar um compromisso na agenda do RH.</summary>
public class EventoAgendaCreateDto
{
    [Required(ErrorMessage = "O título é obrigatório.")]
    [MinLength(2, ErrorMessage = "O título precisa ter pelo menos 2 caracteres.")]
    public string Titulo { get; set; } = string.Empty;

    public string Descricao { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a data/hora do evento.")]
    public DateTime DataEvento { get; set; }

    public TipoEventoAgenda Tipo { get; set; } = TipoEventoAgenda.Outro;

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
