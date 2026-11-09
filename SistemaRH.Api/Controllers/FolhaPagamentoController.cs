using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>Folha de pagamento (holerites) dos funcionários.</summary>
[ApiController]
[Route("api/folha-pagamento")]
[Authorize(Policy = "RhOuAdmin")]
public class FolhaPagamentoController : ControllerBase
{
    private readonly IFolhaPagamentoService _folhaPagamentoService;

    public FolhaPagamentoController(IFolhaPagamentoService folhaPagamentoService)
    {
        _folhaPagamentoService = folhaPagamentoService;
    }

    /// <summary>Lista as folhas de pagamento de um funcionário específico.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<FolhaPagamentoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<FolhaPagamentoDto>>> Listar([FromQuery] int funcionarioId)
    {
        if (funcionarioId <= 0)
        {
            return BadRequest(new { mensagem = "Informe o funcionarioId na query string." });
        }

        return Ok(await _folhaPagamentoService.ListarPorFuncionarioAsync(funcionarioId));
    }

    [HttpPost]
    [ProducesResponseType(typeof(FolhaPagamentoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FolhaPagamentoDto>> Criar([FromQuery] int funcionarioId, FolhaPagamentoCreateDto dto)
    {
        if (funcionarioId <= 0)
        {
            return BadRequest(new { mensagem = "Informe o funcionarioId na query string." });
        }

        var folha = await _folhaPagamentoService.CriarAsync(funcionarioId, dto);
        return CreatedAtAction(nameof(Listar), new { funcionarioId }, folha);
    }
}
