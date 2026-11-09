namespace SistemaRH.Api.Models;

/// <summary>Um cargo/função (ex.: "Desenvolvedor"), com seu salário base de referência.</summary>
public class Cargo
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal SalarioBase { get; set; }
    public string Descricao { get; set; } = string.Empty;

    public List<Funcionario> Funcionarios { get; set; } = new();
}
