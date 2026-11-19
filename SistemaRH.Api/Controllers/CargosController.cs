using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Cargos/funções. Tudo aqui é só RH/Admin — inclusive listar, porque cada
/// cargo traz o salário base (antes qualquer conta logada lia a tabela de
/// salários).
/// </summary>
[ApiController]
[Route("api/cargos")]
[Authorize(Policy = "RhOuAdmin")]
public class CargosController : ControllerBase
{
    private readonly ICargoService _cargoService;

    public CargosController(ICargoService cargoService)
    {
        _cargoService = cargoService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<CargoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CargoDto>>> Listar()
    {
        return Ok(await _cargoService.ListarAsync());
    }

    [HttpPost]
    [Authorize(Policy = "RhOuAdmin")]
    [ProducesResponseType(typeof(CargoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoDto>> Criar(CargoCreateDto dto)
    {
        var cargo = await _cargoService.CriarAsync(dto);
        return CreatedAtAction(nameof(Listar), cargo);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "RhOuAdmin")]
    [ProducesResponseType(typeof(CargoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargoDto>> Atualizar(int id, CargoCreateDto dto)
    {
        return Ok(await _cargoService.AtualizarAsync(id, dto));
    }
}
