using System.ComponentModel.DataAnnotations;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados que o app/site envia pra criar uma conta.</summary>
public class RegisterDto
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [MinLength(2, ErrorMessage = "O nome precisa ter pelo menos 2 caracteres.")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "A senha é obrigatória.")]
    [MinLength(6, ErrorMessage = "A senha precisa ter pelo menos 6 caracteres.")]
    public string Senha { get; set; } = string.Empty;

    public string Cargo { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;

    /// <summary>
    /// Saldo inicial do banco de horas, em minutos (pode ser negativo).
    /// Opcional — usado principalmente para criar cenários de teste/demonstração;
    /// um cadastro normal simplesmente deixa em 0.
    /// </summary>
    public int SaldoBancoHorasMinutos { get; set; } = 0;
}

/// <summary>Dados que o app/site envia pra entrar (tela de login).</summary>
public class LoginDto
{
    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "A senha é obrigatória.")]
    public string Senha { get; set; } = string.Empty;
}

/// <summary>Dados públicos do usuário — nunca inclui a senha/hash.</summary>
public class UsuarioDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    public int SaldoBancoHorasMinutos { get; set; }

    /// <summary>Quantas ausências/justificativas esse usuário já abriu pro RH, no total.</summary>
    public int TotalSolicitacoesAusencia { get; set; }
}

/// <summary>O que a API devolve depois de um login (ou registro) bem-sucedido.</summary>
public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public UsuarioDto Usuario { get; set; } = new();
}
