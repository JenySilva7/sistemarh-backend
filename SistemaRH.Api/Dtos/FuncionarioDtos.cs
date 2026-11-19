using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SistemaRH.Api.Common;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

// Os campos obrigatórios de número/data são anuláveis de propósito: sem
// isso, um campo esquecido no JSON viraria 0 / 01/01/0001 em silêncio (e um
// departamentoId ausente dava 404 "Departamento informado não existe." em
// vez de "Selecione o departamento."). Os textos são validados no serviço
// (Common/Textos), que limpa espaços e caracteres invisíveis antes de contar
// o tamanho e devolve uma mensagem só por campo.

/// <summary>Dados pra cadastrar um novo funcionário.</summary>
public class FuncionarioCreateDto
{
    public string? Nome { get; set; }
    public string? Cpf { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }

    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataNascimento { get; set; }

    [Required(ErrorMessage = "A data de admissão é obrigatória.")]
    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataAdmissao { get; set; }

    [Required(ErrorMessage = "Selecione o departamento.")]
    public int? DepartamentoId { get; set; }

    [Required(ErrorMessage = "Selecione o cargo.")]
    public int? CargoId { get; set; }

    /// <summary>
    /// Gestor direto (outro funcionário já cadastrado e ativo). Opcional —
    /// vazio significa que esse funcionário está no topo da hierarquia.
    /// </summary>
    public int? GerenteId { get; set; }

    /// <summary>Opcional: se vazio, usa o salário base do cargo escolhido.</summary>
    public decimal? Salario { get; set; }

    /// <summary>Opcional: se vazio, usa o valor padrão (ver FuncionarioService.ValeAlimentacaoPadrao).</summary>
    public decimal? ValeAlimentacao { get; set; }

    /// <summary>
    /// Opcional: se informada, já cria o login do app desse colaborador com
    /// essa senha — mesmas regras de POST /api/funcionarios/{id}/acesso-app.
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
    public string? Nome { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }

    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataNascimento { get; set; }

    [Required(ErrorMessage = "Selecione o departamento.")]
    public int? DepartamentoId { get; set; }

    [Required(ErrorMessage = "Selecione o cargo.")]
    public int? CargoId { get; set; }

    public int? GerenteId { get; set; }

    /// <summary>Versão que estava na tela (ver Funcionario.Versao). Se outra pessoa gravou antes, 409.</summary>
    public int? Versao { get; set; }
}

/// <summary>Dados pra desligar um funcionário (encerrar o vínculo). O corpo inteiro é opcional.</summary>
public class DesligarFuncionarioDto
{
    /// <summary>Da admissão até hoje. Se não informada, usa o dia de hoje.</summary>
    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataDesligamento { get; set; }

    public int? Versao { get; set; }
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

    /// <summary>Token de concorrência: o painel devolve esse número nas rotas de escrita.</summary>
    public int Versao { get; set; }
}

/// <summary>
/// Resposta de POST /api/funcionarios/{id}/desligar: o funcionário já
/// desligado e o que aconteceu com os subordinados diretos dele (movidos na
/// mesma transação pro gestor da pessoa desligada, ou deixados sem gestor).
/// </summary>
public class DesligamentoResultadoDto
{
    public FuncionarioDto Funcionario { get; set; } = new();
    public int SubordinadosRealocados { get; set; }

    /// <summary>Gestor que recebeu os subordinados; null se eles ficaram sem gestor (ou não havia nenhum).</summary>
    public string? NovoGestorNome { get; set; }
}

/// <summary>
/// Resposta de POST /api/funcionarios/{id}/promocao. Quando o departamento
/// muda, os subordinados diretos que ficam no departamento antigo passam pro
/// gestor anterior da pessoa promovida; <see cref="SubordinadosRealocados"/> diz quantos.
/// </summary>
public class PromocaoResultadoDto
{
    public FuncionarioDto Funcionario { get; set; } = new();
    public int SubordinadosRealocados { get; set; }
}

// ── Remuneração (salário, vale alimentação e promoção) ──────────────────

/// <summary>Novo salário de um funcionário (PUT /api/funcionarios/{id}/salario).</summary>
public class AjusteSalarioDto
{
    [Required(ErrorMessage = "Informe o novo salário.")]
    public decimal? NovoSalario { get; set; }

    [Required(ErrorMessage = "Informe a data de vigência.")]
    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataVigencia { get; set; }

    public string? Motivo { get; set; }

    public int? Versao { get; set; }
}

/// <summary>Novo valor do vale alimentação (PUT /api/funcionarios/{id}/vale-alimentacao).</summary>
public class AjusteValeAlimentacaoDto
{
    [Required(ErrorMessage = "Informe o novo valor do vale alimentação.")]
    public decimal? NovoValor { get; set; }

    [Required(ErrorMessage = "Informe a data de vigência.")]
    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataVigencia { get; set; }

    public string? Motivo { get; set; }

    public int? Versao { get; set; }
}

/// <summary>Promoção: novo cargo e/ou departamento, com salário igual ou maior (POST /api/funcionarios/{id}/promocao).</summary>
public class PromocaoDto
{
    [Required(ErrorMessage = "Selecione o novo cargo.")]
    public int? NovoCargoId { get; set; }

    [Required(ErrorMessage = "Selecione o novo departamento.")]
    public int? NovoDepartamentoId { get; set; }

    [Required(ErrorMessage = "Informe o novo salário.")]
    public decimal? NovoSalario { get; set; }

    [Required(ErrorMessage = "Informe a data de vigência.")]
    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? DataVigencia { get; set; }

    public string? Motivo { get; set; }

    /// <summary>Opcional: novo gestor direto. null (ou ausente) mantém o atual.</summary>
    public int? NovoGerenteId { get; set; }

    public int? Versao { get; set; }
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
