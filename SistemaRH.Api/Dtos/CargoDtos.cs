using System.ComponentModel.DataAnnotations;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra criar ou editar um cargo.</summary>
public class CargoCreateDto
{
    [Required(ErrorMessage = "O nome do cargo é obrigatório.")]
    [MinLength(2, ErrorMessage = "O nome precisa ter pelo menos 2 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "O salário base não pode ser negativo.")]
    public decimal SalarioBase { get; set; }

    public string Descricao { get; set; } = string.Empty;
}

/// <summary>O que a API devolve ao listar ou consultar um cargo.</summary>
public class CargoDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal SalarioBase { get; set; }
    public string Descricao { get; set; } = string.Empty;
}
