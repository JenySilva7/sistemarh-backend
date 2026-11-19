namespace SistemaRH.Api.Models;

/// <summary>
/// Em que sistema essa conta pode mexer. "Funcionario" é o colaborador comum
/// (app); "Rh" e "Admin" são contas do time de RH (site). O perfil vai no
/// token (claim "perfil") e é ele que as policies "RhOuAdmin" e "Admin"
/// conferem (ver Program.cs). Só um Admin muda o perfil de alguém
/// (PUT /api/usuarios/{id}/perfil).
/// </summary>
public enum PerfilUsuario
{
    Funcionario = 0,
    Rh = 1,
    Admin = 2,
}

/// <summary>
/// Representa uma conta que consegue entrar no app e no site.
/// A senha NUNCA é guardada em texto puro — só o hash gerado pelo PasswordHasher
/// (ver Services/AuthService.cs) fica salvo em <see cref="SenhaHash"/>.
/// </summary>
public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    public PerfilUsuario Perfil { get; set; } = PerfilUsuario.Funcionario;

    /// <summary>
    /// Saldo do banco de horas, em minutos (pode ser negativo). Guardamos em
    /// minutos — e não em horas fracionárias — pra evitar erro de arredondamento
    /// ao exibir algo como "+6h30".
    ///
    /// ATENÇÃO: isso é um campo "achatado" (um número só), enquanto
    /// <see cref="LancamentoBancoHoras"/> guarda o extrato completo por
    /// <see cref="Funcionario"/> (crédito/débito, auditável). Os dois ainda
    /// não estão unificados — fica como próximo passo quando o app passar a
    /// ler o saldo do funcionário ligado a essa conta, em vez desse campo.
    /// </summary>
    public int SaldoBancoHorasMinutos { get; set; } = 0;

    /// <summary>
    /// Link opcional pro cadastro completo de RH (CPF, cargo, departamento
    /// etc.). Nem toda conta tem um funcionário vinculado ainda.
    /// </summary>
    public int? FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }

    /// <summary>
    /// Versão dos tokens dessa conta. Vai no JWT (claim "tv") e é conferida
    /// a cada requisição: quando sobe (logout, troca de senha, desligamento,
    /// troca de perfil ou de e-mail), todo token emitido antes para de valer
    /// na hora — sem isso, um token roubado ou de alguém desligado continuava
    /// funcionando até expirar.
    /// </summary>
    public int TokenVersao { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public List<RegistroPonto> RegistrosPonto { get; set; } = new();
    public List<Ausencia> Ausencias { get; set; } = new();
    public List<MensagemRh> MensagensRh { get; set; } = new();
}
