using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Common;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Contas de acesso e perfis (tela de Permissões do painel). RH vê a lista;
/// só Admin muda o perfil de alguém.
/// </summary>
[ApiController]
[Route("api/usuarios")]
[Authorize(Policy = "RhOuAdmin")]
public class UsuariosController : ControllerBase
{
    private readonly IUsuarioService _usuarioService;

    public UsuariosController(IUsuarioService usuarioService)
    {
        _usuarioService = usuarioService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<UsuarioListaDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UsuarioListaDto>>> Listar()
    {
        return Ok(await _usuarioService.ListarAsync());
    }

    /// <summary>
    /// Troca o perfil (0 = Funcionario, 1 = Rh, 2 = Admin). Ninguém muda o
    /// próprio perfil, o sistema nunca fica sem Admin e os tokens da conta
    /// alterada deixam de valer (o perfil vai dentro do token). Quando o
    /// perfil sobe, exige uma senha provisória (senhaProvisoria), que
    /// substitui a senha atual.
    /// </summary>
    [HttpPut("{id:int}/perfil")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(UsuarioListaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UsuarioListaDto>> AlterarPerfil(int id, AlterarPerfilUsuarioDto dto)
    {
        return Ok(await _usuarioService.AlterarPerfilAsync(this.UsuarioIdAutenticado(), id, dto));
    }
}
