using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Common;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// CRUD de justificativas de ausência. Todas as rotas exigem autenticação
/// e são restritas ao próprio usuário (ver AusenciaService).
/// </summary>
[ApiController]
[Route("api/ausencias")]
[Authorize]
public class AusenciasController : ControllerBase
{
    private readonly IAusenciaService _ausenciaService;

    public AusenciasController(IAusenciaService ausenciaService)
    {
        _ausenciaService = ausenciaService;
    }

    /// <summary>Lista as justificativas de ausência do usuário autenticado.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<AusenciaDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AusenciaDto>>> Listar()
    {
        var ausencias = await _ausenciaService.ListarAsync(this.UsuarioIdAutenticado());
        return Ok(ausencias);
    }

    /// <summary>Consulta uma justificativa específica.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AusenciaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AusenciaDto>> ObterPorId(int id)
    {
        var ausencia = await _ausenciaService.ObterPorIdAsync(this.UsuarioIdAutenticado(), id);
        return Ok(ausencia);
    }

    /// <summary>Cria uma nova justificativa de ausência (status inicial: Enviada).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AusenciaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AusenciaDto>> Criar(AusenciaCreateDto dto)
    {
        var ausencia = await _ausenciaService.CriarAsync(this.UsuarioIdAutenticado(), dto);
        return CreatedAtAction(nameof(ObterPorId), new { id = ausencia.Id }, ausencia);
    }

    /// <summary>Atualiza uma justificativa — só permitido enquanto o status for "Enviada".</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(AusenciaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AusenciaDto>> Atualizar(int id, AusenciaCreateDto dto)
    {
        var ausencia = await _ausenciaService.AtualizarAsync(this.UsuarioIdAutenticado(), id, dto);
        return Ok(ausencia);
    }

    /// <summary>Cancela (exclui) uma justificativa — só permitido enquanto o status for "Enviada".</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancelar(int id)
    {
        await _ausenciaService.CancelarAsync(this.UsuarioIdAutenticado(), id);
        return NoContent();
    }
}
