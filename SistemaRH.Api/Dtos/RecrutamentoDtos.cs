using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SistemaRH.Api.Common;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra cadastrar um candidato num processo seletivo (textos validados no RecrutamentoService).</summary>
public class RecrutamentoCreateDto
{
    public string? NomeCandidato { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Vaga { get; set; }

    [Required(ErrorMessage = "Informe a data da candidatura.")]
    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataCandidatura { get; set; }

    public string? Observacoes { get; set; }
}

/// <summary>Dados pra atualizar o status de um candidato no processo seletivo.</summary>
public class AtualizarStatusRecrutamentoDto
{
    [Required(ErrorMessage = "Status inválido.")]
    public StatusRecrutamento? Status { get; set; }

    public string? Observacoes { get; set; }
}

/// <summary>O que a API devolve ao listar ou consultar um candidato.</summary>
public class RecrutamentoDto
{
    public int Id { get; set; }
    public string NomeCandidato { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string Vaga { get; set; } = string.Empty;
    public DateTime DataCandidatura { get; set; }
    public StatusRecrutamento Status { get; set; }
    public string Observacoes { get; set; } = string.Empty;
}
