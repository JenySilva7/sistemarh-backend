using System.ComponentModel.DataAnnotations;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>
/// Dados que um Admin envia pra criar uma conta de acesso (POST /api/auth/register).
/// Só esses campos são lidos: qualquer outro que vier no JSON (cargo,
/// departamento, saldo do banco de horas...) é ignorado — antes o cadastro
/// era público e aceitava até o próprio saldo de horas.
/// </summary>
public class RegisterDto
{
    // As regras (tamanho, formato, caracteres permitidos) ficam no
    // AuthService, com as mensagens em português — ver Common/Textos.
    public string? Nome { get; set; }
    public string? Email { get; set; }
    public string? Senha { get; set; }

    /// <summary>0 = Funcionario (padrão), 1 = Rh, 2 = Admin.</summary>
    public PerfilUsuario? Perfil { get; set; }
}

/// <summary>Dados que o app/site envia pra entrar (tela de login).</summary>
public class LoginDto
{
    [Required(ErrorMessage = "O e-mail é obrigatório.")]
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

    // Preenchidos só quando a conta está vinculada a um cadastro de
    // funcionário; nesse caso, Cargo e Departamento também vêm de lá.
    public int? FuncionarioId { get; set; }
    public DateTime? DataAdmissao { get; set; }
    public string? GerenteNome { get; set; }
}

/// <summary>O que a API devolve depois de um login bem-sucedido.</summary>
public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public UsuarioDto Usuario { get; set; } = new();
}

/// <summary>
/// Uma conta de acesso, do jeito que a tela de Permissões vê (GET /api/usuarios)
/// e do jeito que o register devolve a conta criada.
/// </summary>
public class UsuarioListaDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public PerfilUsuario Perfil { get; set; }
    public int? FuncionarioId { get; set; }
    public string? FuncionarioNome { get; set; }
    public bool FuncionarioDesligado { get; set; }
}

/// <summary>Novo perfil de uma conta (PUT /api/usuarios/{id}/perfil, só Admin).</summary>
public class AlterarPerfilUsuarioDto
{
    [Required(ErrorMessage = "Selecione o perfil.")]
    public PerfilUsuario? Perfil { get; set; }

    /// <summary>
    /// Perfil que a tela estava mostrando (opcional). Se não for mais o atual
    /// (outro Admin mudou antes), a resposta é 409.
    /// </summary>
    public PerfilUsuario? PerfilAtual { get; set; }

    /// <summary>
    /// Senha provisória, definida pelo Admin. Obrigatória quando o perfil SOBE
    /// (Funcionário → RH, Funcionário → Admin, RH → Admin): ela substitui a
    /// senha atual, que pode ter sido definida pelo RH no cadastro ou no
    /// "Criar acesso ao app". Ignorada quando o perfil desce ou não muda.
    /// </summary>
    public string? SenhaProvisoria { get; set; }
}

/// <summary>Troca da própria senha (PUT /api/auth/senha).</summary>
public class TrocarSenhaDto
{
    public string? SenhaAtual { get; set; }
    public string? NovaSenha { get; set; }
}

/// <summary>Token novo, devolvido depois de trocar a senha (os antigos deixam de valer).</summary>
public class TokenDto
{
    public string Token { get; set; } = string.Empty;
}
