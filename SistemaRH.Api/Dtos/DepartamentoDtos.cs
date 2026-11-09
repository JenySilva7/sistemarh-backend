using System.ComponentModel.DataAnnotations;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra criar ou editar um departamento.</summary>
public class DepartamentoCreateDto
{
    [Required(ErrorMessage = "O nome do departamento é obrigatório.")]
    [MinLength(2, ErrorMessage = "O nome precisa ter pelo menos 2 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    public string Descricao { get; set; } = string.Empty;
}

/// <summary>O que a API devolve ao listar ou consultar um departamento.</summary>
public class DepartamentoDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
}
