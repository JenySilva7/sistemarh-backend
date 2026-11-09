using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Common;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Rotas de autenticação: cadastro, login e "quem sou eu".
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

    /// <summary>Cria uma conta nova. Pública — não exige estar logado.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        var resultado = await _authService.RegistrarAsync(dto);
        return CreatedAtAction(nameof(Me), null, resultado);
    }

    /// <summary>Autentica com e-mail e senha e devolve um token JWT. Pública.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var resultado = await _authService.LoginAsync(dto);
        return Ok(resultado);
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
