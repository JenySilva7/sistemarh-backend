using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>Avaliações de desempenho dos funcionários.</summary>
[ApiController]
[Route("api/avaliacoes")]
[Authorize(Policy = "RhOuAdmin")]
public class AvaliacoesController : ControllerBase
{
    private readonly IAvaliacaoService _avaliacaoService;

    public AvaliacoesController(IAvaliacaoService avaliacaoService)
    {
        _avaliacaoService = avaliacaoService;
    }

    /// <summary>Lista as avaliações de um funcionário específico.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<AvaliacaoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<AvaliacaoDto>>> Listar([FromQuery] int funcionarioId)
    {
        if (funcionarioId <= 0)
        {
            return BadRequest(new { mensagem = "Informe o funcionarioId na query string." });
        }

        return Ok(await _avaliacaoService.ListarPorFuncionarioAsync(funcionarioId));
    }

    [HttpPost]
    [ProducesResponseType(typeof(AvaliacaoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AvaliacaoDto>> Criar([FromQuery] int funcionarioId, AvaliacaoCreateDto dto)
    {
        if (funcionarioId <= 0)
        {
            return BadRequest(new { mensagem = "Informe o funcionarioId na query string." });
        }

        var avaliacao = await _avaliacaoService.CriarAsync(funcionarioId, dto);
        return CreatedAtAction(nameof(Listar), new { funcionarioId }, avaliacao);
    }
}
