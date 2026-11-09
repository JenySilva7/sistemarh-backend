using System.ComponentModel.DataAnnotations;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra cadastrar um novo funcionário.</summary>
public class FuncionarioCreateDto
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [MinLength(2, ErrorMessage = "O nome precisa ter pelo menos 2 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "O CPF é obrigatório.")]
    public string Cpf { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string? Email { get; set; }

    public string? Telefone { get; set; }
    public DateTime? DataNascimento { get; set; }

    [Required(ErrorMessage = "A data de admissão é obrigatória.")]
    public DateTime DataAdmissao { get; set; }

    [Required(ErrorMessage = "Selecione o departamento.")]
    public int DepartamentoId { get; set; }

    [Required(ErrorMessage = "Selecione o cargo.")]
    public int CargoId { get; set; }

    /// <summary>
    /// Gestor direto (outro funcionário já cadastrado). Opcional — vazio
    /// significa que esse funcionário está no topo da hierarquia.
    /// </summary>
    public int? GerenteId { get; set; }
}

/// <summary>Dados editáveis de um funcionário já cadastrado.</summary>
public class FuncionarioUpdateDto
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [MinLength(2, ErrorMessage = "O nome precisa ter pelo menos 2 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string? Email { get; set; }

    public string? Telefone { get; set; }
    public DateTime? DataNascimento { get; set; }

    [Required(ErrorMessage = "Selecione o departamento.")]
    public int DepartamentoId { get; set; }

    [Required(ErrorMessage = "Selecione o cargo.")]
    public int CargoId { get; set; }

    public int? GerenteId { get; set; }
}

/// <summary>Dados pra desligar um funcionário (encerrar o vínculo).</summary>
public class DesligarFuncionarioDto
{
    /// <summary>Se não informada, usa a data/hora atual.</summary>
    public DateTime? DataDesligamento { get; set; }
}

/// <summary>O que a API devolve ao listar ou consultar um funcionário.</summary>
public class FuncionarioDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public DateTime? DataNascimento { get; set; }
    public DateTime DataAdmissao { get; set; }
    public DateTime? DataDesligamento { get; set; }
    public StatusFuncionario Status { get; set; }
    public int DepartamentoId { get; set; }
    public string DepartamentoNome { get; set; } = string.Empty;
    public int CargoId { get; set; }
    public string CargoNome { get; set; } = string.Empty;

    public int? GerenteId { get; set; }
    public string? GerenteNome { get; set; }

    /// <summary>
    /// Quantas ausências/justificativas esse funcionário já abriu pro RH, no
    /// total. Calculado contando as <see cref="Ausencia"/> de qualquer
    /// <see cref="Usuario"/> vinculado a este funcionário (via
    /// <see cref="Usuario.FuncionarioId"/>) — fica 0 se nenhuma conta de
    /// login ainda foi linkada a esse cadastro.
    /// </summary>
    public int TotalSolicitacoesAusencia { get; set; }
}
