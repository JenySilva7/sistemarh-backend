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

    /// <summary>Opcional: se vazio, usa o salário base do cargo escolhido.</summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "O salário precisa ser maior que zero.")]
    public decimal? Salario { get; set; }

    /// <summary>Opcional: se vazio, usa o valor padrão (ver FuncionarioService.ValeAlimentacaoPadrao).</summary>
    [Range(0, double.MaxValue, ErrorMessage = "O vale alimentação não pode ser negativo.")]
    public decimal? ValeAlimentacao { get; set; }

    /// <summary>
    /// Opcional: se informada, já cria (ou vincula) o login do app desse
    /// colaborador com essa senha — mesmas regras de POST /api/funcionarios/{id}/acesso-app.
    /// </summary>
    public string? SenhaAcessoApp { get; set; }
}

/// <summary>Senha que o RH define pro colaborador entrar no app (cria o acesso ou redefine a senha).</summary>
public class DefinirAcessoAppDto
{
    // Anulável e sem [MinLength] de propósito: a senha é validada no serviço,
    // depois de conferir se o funcionário existe/está ativo/tem e-mail, e
    // sempre com a mensagem em português.
    public string? Senha { get; set; }
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

    public decimal Salario { get; set; }
    public decimal ValeAlimentacao { get; set; }

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

    /// <summary>Se existe uma conta de login (<see cref="Usuario"/>) vinculada a esse funcionário.</summary>
    public bool PossuiAcessoApp { get; set; }
}

// ── Remuneração (salário, vale alimentação e promoção) ──────────────────
// Os campos "obrigatórios" de valor/data são anuláveis de propósito: sem
// isso, um campo esquecido no JSON viraria 0 / 01/01/0001 em silêncio, em
// vez de cair no [Required] com a mensagem certa.

/// <summary>Novo salário de um funcionário (PUT /api/funcionarios/{id}/salario).</summary>
public class AjusteSalarioDto
{
    [Required(ErrorMessage = "Informe o novo salário.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "O salário precisa ser maior que zero.")]
    public decimal? NovoSalario { get; set; }

    [Required(ErrorMessage = "Informe a data de vigência.")]
    public DateTime? DataVigencia { get; set; }

    [Required(ErrorMessage = "Informe o motivo do ajuste.")]
    [MinLength(3, ErrorMessage = "O motivo precisa ter pelo menos 3 caracteres.")]
    public string Motivo { get; set; } = string.Empty;
}

/// <summary>Novo valor do vale alimentação (PUT /api/funcionarios/{id}/vale-alimentacao).</summary>
public class AjusteValeAlimentacaoDto
{
    [Required(ErrorMessage = "Informe o novo valor do vale alimentação.")]
    [Range(0, double.MaxValue, ErrorMessage = "O vale alimentação não pode ser negativo.")]
    public decimal? NovoValor { get; set; }

    [Required(ErrorMessage = "Informe a data de vigência.")]
    public DateTime? DataVigencia { get; set; }

    [Required(ErrorMessage = "Informe o motivo do ajuste.")]
    [MinLength(3, ErrorMessage = "O motivo precisa ter pelo menos 3 caracteres.")]
    public string Motivo { get; set; } = string.Empty;
}

/// <summary>Promoção: novo cargo, departamento e salário de uma vez (POST /api/funcionarios/{id}/promocao).</summary>
public class PromocaoDto
{
    [Required(ErrorMessage = "Selecione o novo cargo.")]
    public int NovoCargoId { get; set; }

    [Required(ErrorMessage = "Selecione o novo departamento.")]
    public int NovoDepartamentoId { get; set; }

    [Required(ErrorMessage = "Informe o novo salário.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "O salário precisa ser maior que zero.")]
    public decimal? NovoSalario { get; set; }

    [Required(ErrorMessage = "Informe a data de vigência.")]
    public DateTime? DataVigencia { get; set; }

    [Required(ErrorMessage = "Informe o motivo do ajuste.")]
    [MinLength(3, ErrorMessage = "O motivo precisa ter pelo menos 3 caracteres.")]
    public string Motivo { get; set; } = string.Empty;
}

/// <summary>Uma linha do histórico de remuneração, do jeito que a API devolve.</summary>
public class AjusteRemuneracaoDto
{
    public int Id { get; set; }
    public TipoAjusteRemuneracao Tipo { get; set; }
    public decimal ValorAnterior { get; set; }
    public decimal ValorNovo { get; set; }

    // Só vêm preenchidos numa promoção.
    public string? CargoAnteriorNome { get; set; }
    public string? CargoNovoNome { get; set; }
    public string? DepartamentoAnteriorNome { get; set; }
    public string? DepartamentoNovoNome { get; set; }

    /// <summary>Data de calendário (sem "Z").</summary>
    public DateTime DataVigencia { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? ResponsavelNome { get; set; }

    /// <summary>Instante em que o ajuste foi registrado (UTC, com "Z").</summary>
    public DateTime CriadoEm { get; set; }
}
