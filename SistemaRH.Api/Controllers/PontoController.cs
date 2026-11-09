using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Common;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Rotas do ponto eletrônico. Todas exigem autenticação — cada usuário só
/// vê e registra o próprio ponto (o Id do usuário vem do token, nunca de um
/// parâmetro da URL, então não tem como consultar o ponto de outra pessoa).
/// </summary>
[ApiController]
[Route("api/ponto")]
[Authorize]
public class PontoController : ControllerBase
{
    private readonly IPontoService _pontoService;

    public PontoController(IPontoService pontoService)
    {
        _pontoService = pontoService;
    }

    /// <summary>Lista as marcações de ponto de hoje do usuário autenticado.</summary>
    [HttpGet("hoje")]
    [ProducesResponseType(typeof(List<RegistroPontoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RegistroPontoDto>>> ListarDeHoje()
    {
        var registros = await _pontoService.ListarRegistrosDeHojeAsync(this.UsuarioIdAutenticado());
        return Ok(registros);
    }

    /// <summary>
    /// Registra a próxima marcação pendente do dia (Entrada → Saída almoço →
    /// Volta almoço → Saída). O cliente não escolhe o tipo — a API decide
    /// qual é a próxima, igual ao botão único "Registrar" do app.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RegistrarPontoResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegistrarPontoResponseDto>> RegistrarProximo()
    {
        var resultado = await _pontoService.RegistrarProximoAsync(this.UsuarioIdAutenticado());
        return CreatedAtAction(nameof(ListarDeHoje), null, resultado);
    }
}
