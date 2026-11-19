using System.ComponentModel.DataAnnotations;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra criar ou editar um cargo.</summary>
public class CargoCreateDto
{
    // Nome (2 a 100), salário base (R$ 0,00 a R$ 1.000.000,00, 2 casas) e
    // descrição (até 500) são validados no CargoService.
    public string? Nome { get; set; }

    /// <summary>Opcional (padrão R$ 0,00).</summary>
    public decimal? SalarioBase { get; set; }

    public string? Descricao { get; set; }
}

/// <summary>O que a API devolve ao listar ou consultar um cargo.</summary>
public class CargoDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal SalarioBase { get; set; }
    public string Descricao { get; set; } = string.Empty;
}
