using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Common;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// "Falar com o RH" do lado do colaborador (app). O lado do RH (listar e
/// responder) fica em /api/rh/mensagens, no RhController.
/// </summary>
[ApiController]
[Route("api/mensagens-rh")]
[Authorize]
public class MensagensRhController : ControllerBase
{
    private readonly IMensagemRhService _mensagemRhService;

    public MensagensRhController(IMensagemRhService mensagemRhService)
    {
        _mensagemRhService = mensagemRhService;
    }

    /// <summary>Envia uma mensagem ao RH (conta ligada a um colaborador ativo).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(MensagemRhDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MensagemRhDto>> Enviar(MensagemRhCreateDto dto)
    {
        var mensagem = await _mensagemRhService.CriarAsync(this.UsuarioIdAutenticado(), dto);
        return CreatedAtAction(nameof(ListarMinhas), null, mensagem);
    }

    /// <summary>Mensagens da própria pessoa, das mais novas pras mais antigas (com a resposta do RH, se houver).</summary>
    [HttpGet("minhas")]
    [ProducesResponseType(typeof(List<MensagemRhDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MensagemRhDto>>> ListarMinhas()
    {
        return Ok(await _mensagemRhService.ListarMinhasAsync(this.UsuarioIdAutenticado()));
    }
}
