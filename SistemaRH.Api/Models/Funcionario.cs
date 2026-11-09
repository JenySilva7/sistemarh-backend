namespace SistemaRH.Api.Models;

public enum StatusFuncionario
{
    Ativo = 0,
    Desligado = 1,
}

/// <summary>
/// Dados cadastrais completos de um colaborador, usados pelo lado
/// administrativo/RH (site): CPF, cargo, departamento, datas de
/// admissão/desligamento etc.
///
/// É um cadastro diferente de <see cref="Usuario"/>, que é só a conta de
/// login usada pelo app — um funcionário pode existir aqui sem nunca ter
/// feito login no app, e vice-versa. O campo opcional <see cref="Usuario.FuncionarioId"/>
/// é quem liga as duas pontas quando necessário.
/// </summary>
public class Funcionario
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public DateTime? DataNascimento { get; set; }
    public DateTime DataAdmissao { get; set; }
    public DateTime? DataDesligamento { get; set; }
    public StatusFuncionario Status { get; set; } = StatusFuncionario.Ativo;

    public int DepartamentoId { get; set; }
    public Departamento? Departamento { get; set; }

    public int CargoId { get; set; }
    public Cargo? Cargo { get; set; }

    /// <summary>
    /// Gestor direto desse funcionário (outro Funcionario). Nulo pra quem
    /// está no topo da hierarquia (ex.: CEO). É essa relação que monta o
    /// organograma — cada funcionário "pendura" embaixo do seu gestor.
    /// </summary>
    public int? GerenteId { get; set; }
    public Funcionario? Gerente { get; set; }
    public List<Funcionario> Subordinados { get; set; } = new();

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public List<LancamentoBancoHoras> LancamentosBancoHoras { get; set; } = new();
    public List<Avaliacao> Avaliacoes { get; set; } = new();
    public List<EventoAgenda> EventosAgenda { get; set; } = new();
    public List<FolhaPagamento> FolhasPagamento { get; set; } = new();
}
