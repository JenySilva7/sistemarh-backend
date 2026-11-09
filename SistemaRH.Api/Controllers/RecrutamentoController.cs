using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>Acompanhamento de candidatos em processos seletivos.</summary>
[ApiController]
[Route("api/recrutamento")]
[Authorize(Policy = "RhOuAdmin")]
public class RecrutamentoController : ControllerBase
{
    private readonly IRecrutamentoService _recrutamentoService;

    public RecrutamentoController(IRecrutamentoService recrutamentoService)
    {
        _recrutamentoService = recrutamentoService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<RecrutamentoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RecrutamentoDto>>> Listar([FromQuery] StatusRecrutamento? status)
    {
        return Ok(await _recrutamentoService.ListarAsync(status));
    }

    [HttpPost]
    [ProducesResponseType(typeof(RecrutamentoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RecrutamentoDto>> Criar(RecrutamentoCreateDto dto)
    {
        var recrutamento = await _recrutamentoService.CriarAsync(dto);
        return CreatedAtAction(nameof(Listar), recrutamento);
    }

    [HttpPut("{id:int}/status")]
    [ProducesResponseType(typeof(RecrutamentoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecrutamentoDto>> AtualizarStatus(int id, AtualizarStatusRecrutamentoDto dto)
    {
        return Ok(await _recrutamentoService.AtualizarStatusAsync(id, dto));
    }
}
