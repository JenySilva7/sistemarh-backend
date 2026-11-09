namespace SistemaRH.Api.Models;

/// <summary>
/// Um holerite de um funcionário num mês específico (competência, no
/// formato "AAAA-MM", ex.: "2026-08"). O salário líquido (salário + bônus -
/// descontos) é responsabilidade do serviço que criar/atualizar esse
/// registro calcular — ainda não implementado, só o cadastro da tabela.
/// </summary>
public class FolhaPagamento
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }
    public string Competencia { get; set; } = string.Empty;
    public decimal Salario { get; set; }
    public decimal Bonus { get; set; }
    public decimal Descontos { get; set; }
    public decimal SalarioLiquido { get; set; }
    public DateTime? DataPagamento { get; set; }
}
