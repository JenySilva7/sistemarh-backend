using System.ComponentModel.DataAnnotations;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra cadastrar um candidato num processo seletivo.</summary>
public class RecrutamentoCreateDto
{
    [Required(ErrorMessage = "O nome do candidato é obrigatório.")]
    [MinLength(2, ErrorMessage = "O nome precisa ter pelo menos 2 caracteres.")]
    public string NomeCandidato { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string? Email { get; set; }

    public string? Telefone { get; set; }

    [Required(ErrorMessage = "Informe a vaga.")]
    public string Vaga { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a data da candidatura.")]
    public DateTime DataCandidatura { get; set; }

    public string Observacoes { get; set; } = string.Empty;
}

/// <summary>Dados pra atualizar o status de um candidato no processo seletivo.</summary>
public class AtualizarStatusRecrutamentoDto
{
    [Required(ErrorMessage = "Selecione o novo status.")]
    public StatusRecrutamento Status { get; set; }

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
