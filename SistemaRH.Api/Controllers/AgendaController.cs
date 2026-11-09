using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>Agenda do RH (reuniões, entrevistas, avaliações agendadas...).</summary>
[ApiController]
[Route("api/agenda")]
[Authorize(Policy = "RhOuAdmin")]
public class AgendaController : ControllerBase
{
    private readonly IEventoAgendaService _eventoAgendaService;

    public AgendaController(IEventoAgendaService eventoAgendaService)
    {
        _eventoAgendaService = eventoAgendaService;
    }

    /// <summary>Lista os eventos, opcionalmente filtrando por um intervalo de datas.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<EventoAgendaDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<EventoAgendaDto>>> Listar([FromQuery] DateTime? de, [FromQuery] DateTime? ate)
    {
        return Ok(await _eventoAgendaService.ListarAsync(de, ate));
    }

    [HttpPost]
    [ProducesResponseType(typeof(EventoAgendaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventoAgendaDto>> Criar(EventoAgendaCreateDto dto)
    {
        var evento = await _eventoAgendaService.CriarAsync(dto);
        return CreatedAtAction(nameof(Listar), evento);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remover(int id)
    {
        await _eventoAgendaService.RemoverAsync(id);
        return NoContent();
    }
}
