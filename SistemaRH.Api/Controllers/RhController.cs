using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Acompanhamento, pelo RH, do que os colaboradores enviam pelo app:
/// justificativas de ausência de todos (e a aprovação delas) e as
/// marcações de ponto de um dia.
/// </summary>
[ApiController]
[Route("api/rh")]
[Authorize(Policy = "RhOuAdmin")]
public class RhController : ControllerBase
{
    private readonly IRhService _rhService;

    public RhController(IRhService rhService)
    {
        _rhService = rhService;
    }

    /// <summary>Lista as justificativas de ausência de todos os colaboradores (mais recentes primeiro).</summary>
    [HttpGet("ausencias")]
    [ProducesResponseType(typeof(List<AusenciaRhDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AusenciaRhDto>>> ListarAusencias([FromQuery] StatusAusencia? status)
    {
        return Ok(await _rhService.ListarAusenciasAsync(status));
    }

    /// <summary>Muda o status de uma justificativa (EmAnalise, Aprovada ou Recusada).</summary>
    [HttpPut("ausencias/{id:int}/status")]
    [ProducesResponseType(typeof(AusenciaRhDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
}
