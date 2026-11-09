namespace SistemaRH.Api.Models;

/// <summary>Um setor da empresa (ex.: "Tecnologia da Informação").</summary>
public class Departamento
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public List<Funcionario> Funcionarios { get; set; } = new();
}
