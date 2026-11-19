using System.ComponentModel.DataAnnotations;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra criar ou editar um departamento.</summary>
public class DepartamentoCreateDto
{
    // Nome (2 a 100) e descrição (até 500) são validados no DepartamentoService.
    public string? Nome { get; set; }

    public string? Descricao { get; set; }
}

/// <summary>O que a API devolve ao listar ou consultar um departamento.</summary>
public class DepartamentoDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
}
