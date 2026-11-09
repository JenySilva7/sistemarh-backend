using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Departamentos (setores da empresa). Qualquer usuário autenticado pode
/// listar (ex.: pra preencher um dropdown); só RH/Admin cria ou edita.
/// </summary>
[ApiController]
[Route("api/departamentos")]
[Authorize]
public class DepartamentosController : ControllerBase
{
    private readonly IDepartamentoService _departamentoService;

    public DepartamentosController(IDepartamentoService departamentoService)
    {
        _departamentoService = departamentoService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<DepartamentoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DepartamentoDto>>> Listar()
    {
        return Ok(await _departamentoService.ListarAsync());
    }

    [HttpPost]
    [Authorize(Policy = "RhOuAdmin")]
    [ProducesResponseType(typeof(DepartamentoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartamentoDto>> Criar(DepartamentoCreateDto dto)
    {
        var departamento = await _departamentoService.CriarAsync(dto);
        return CreatedAtAction(nameof(Listar), departamento);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "RhOuAdmin")]
    [ProducesResponseType(typeof(DepartamentoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartamentoDto>> Atualizar(int id, DepartamentoCreateDto dto)
    {
        return Ok(await _departamentoService.AtualizarAsync(id, dto));
    }
}
