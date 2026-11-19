using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Common;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Acompanhamento, pelo RH, do que os colaboradores enviam pelo app:
/// justificativas de ausência de todos (e a aprovação delas), as marcações
/// de ponto de um dia e as mensagens do "Falar com o RH".
/// </summary>
[ApiController]
[Route("api/rh")]
[Authorize(Policy = "RhOuAdmin")]
public class RhController : ControllerBase
{
    private readonly IRhService _rhService;
    private readonly IMensagemRhService _mensagemRhService;

    public RhController(IRhService rhService, IMensagemRhService mensagemRhService)
    {
        _rhService = rhService;
        _mensagemRhService = mensagemRhService;
    }

    /// <summary>Lista as justificativas de ausência de todos os colaboradores (mais recentes primeiro).</summary>
    [HttpGet("ausencias")]
    [ProducesResponseType(typeof(List<AusenciaRhDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AusenciaRhDto>>> ListarAusencias([FromQuery] StatusAusencia? status)
    {
        return Ok(await _rhService.ListarAusenciasAsync(status));
    }

    /// <summary>
    /// Muda o status de uma justificativa (EmAnalise, Aprovada ou Recusada).
    /// Só vale se ela ainda estiver Enviada ou Em análise e, se a versão vier,
    /// se for a mesma que estava na tela (senão 409).
    /// </summary>
    [HttpPut("ausencias/{id:int}/status")]
    [ProducesResponseType(typeof(AusenciaRhDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AusenciaRhDto>> AtualizarStatusAusencia(int id, AtualizarStatusAusenciaDto dto)
    {
        return Ok(await _rhService.AtualizarStatusAusenciaAsync(id, dto));
    }

    /// <summary>
    /// Marcações de ponto de todos num dia (AAAA-MM-DD, no horário de
    /// Brasília). Sem data, usa o dia de hoje.
    /// </summary>
    [HttpGet("ponto")]
    [ProducesResponseType(typeof(List<RegistroPontoRhDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<RegistroPontoRhDto>>> ListarPonto([FromQuery] string? data)
    {
        return Ok(await _rhService.ListarPontoDoDiaAsync(data));
    }

    /// <summary>Mensagens recebidas pelo "Falar com o RH" (mais recentes primeiro), opcionalmente por status.</summary>
    [HttpGet("mensagens")]
    [ProducesResponseType(typeof(List<MensagemRhDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MensagemRhDto>>> ListarMensagens([FromQuery] StatusMensagemRh? status)
    {
        return Ok(await _mensagemRhService.ListarAsync(status));
    }

    /// <summary>Responde uma mensagem. Já respondida ou com versão diferente da tela: 409.</summary>
    [HttpPut("mensagens/{id:int}/resposta")]
    [ProducesResponseType(typeof(MensagemRhDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MensagemRhDto>> ResponderMensagem(int id, ResponderMensagemRhDto dto)
    {
        return Ok(await _mensagemRhService.ResponderAsync(id, dto, this.UsuarioIdAutenticado()));
    }
}
