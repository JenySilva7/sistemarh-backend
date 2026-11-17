namespace SistemaRH.Api.Models;

public enum TipoAjusteRemuneracao
{
    Salario = 0,
    ValeAlimentacao = 1,
    Promocao = 2,
}

/// <summary>
/// Uma linha do histórico de remuneração de um funcionário: cada ajuste de
/// salário, de vale alimentação ou promoção feito pelo RH vira um registro
/// aqui, com o valor antes e depois. O valor atual fica no próprio
/// <see cref="Funcionario"/>; esse histórico só existe pra auditoria (quem
/// mudou, quando, de quanto pra quanto e por quê).
/// </summary>
public class AjusteRemuneracao
{
    public int Id { get; set; }

    public int FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }

    public TipoAjusteRemuneracao Tipo { get; set; }

    /// <summary>
    /// Valores antes e depois do ajuste. Em <see cref="TipoAjusteRemuneracao.ValeAlimentacao"/>
    /// são os valores do vale; nos outros dois tipos, o salário.
    /// </summary>
    public decimal ValorAnterior { get; set; }
    public decimal ValorNovo { get; set; }

    // Cargo e departamento só são preenchidos numa promoção.
    public int? CargoAnteriorId { get; set; }
    public Cargo? CargoAnterior { get; set; }
    public int? CargoNovoId { get; set; }
    public Cargo? CargoNovo { get; set; }
    public int? DepartamentoAnteriorId { get; set; }
    public Departamento? DepartamentoAnterior { get; set; }
    public int? DepartamentoNovoId { get; set; }
    public Departamento? DepartamentoNovo { get; set; }

    /// <summary>
    /// Data de calendário a partir da qual o ajuste vale (sem horário/fuso).
    /// O novo valor é aplicado no cadastro na hora; essa data é só registro.
    /// </summary>
    public DateTime DataVigencia { get; set; }

    public string Motivo { get; set; } = string.Empty;

    /// <summary>Conta de RH/Admin que fez o ajuste (lida do token). Nula se essa conta for excluída depois.</summary>
    public int? ResponsavelUsuarioId { get; set; }
    public Usuario? ResponsavelUsuario { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
