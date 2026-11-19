using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Common;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Rotas de autenticação: login, logout, "quem sou eu" e a criação de contas
/// pelo Admin.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Cria uma conta de acesso. Só Admin: o cadastro era público, e qualquer
    /// pessoa criava conta, batia ponto, mandava justificativa e lia os
    /// salários base dos cargos. Colaborador ganha acesso pelo RH
    /// (POST /api/funcionarios/{id}/acesso-app), não por aqui.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(UsuarioListaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioListaDto>> Register(RegisterDto dto)
    {
        var usuario = await _authService.RegistrarAsync(dto);
        return StatusCode(StatusCodes.Status201Created, usuario);
    }

    /// <summary>
    /// Autentica com e-mail e senha e devolve um token JWT. Pública. Depois de
    /// 5 senhas erradas pro mesmo e-mail (a partir do mesmo IP) em 15 minutos,
    /// ou 20 falhas do mesmo IP somando todos os e-mails, responde 429 por 15 minutos.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        // Atrás do proxy da Railway, o IP real vem do X-Forwarded-For (ver UseForwardedHeaders em Program.cs).
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var resultado = await _authService.LoginAsync(dto, ip);
        return Ok(resultado);
    }

    /// <summary>Encerra a sessão: invalida TODOS os tokens dessa conta (outras abas e aparelhos também).</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        await _authService.LogoutAsync(this.UsuarioIdAutenticado());
        return NoContent();
    }

    /// <summary>
    /// A própria pessoa troca a senha, confirmando a atual. A senha atual
    /// errada conta no limite de tentativas do login (429 depois de 5). Todas
    /// as sessões da conta caem, e a resposta traz um token novo pra esta.
    /// </summary>
    [HttpPut("senha")]
    [Authorize]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TokenDto>> TrocarSenha(TrocarSenhaDto dto)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        return Ok(await _authService.TrocarSenhaAsync(this.UsuarioIdAutenticado(), dto, ip));
    }

    /// <summary>Devolve os dados do usuário dono do token enviado. Exige autenticação.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UsuarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UsuarioDto>> Me()
    {
        var usuario = await _authService.ObterUsuarioAsync(this.UsuarioIdAutenticado());
        return Ok(usuario);
    }
}
