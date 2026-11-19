using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Common;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Dados da própria pessoa logada, pra qualquer perfil (o "Meus Dados" do
/// painel e o Holerite/"Meus dados" do app). O colaborador é sempre o ligado
/// à conta do token — não existe parâmetro de Id aqui.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IMeService _meService;

    public MeController(IMeService meService)
    {
        _meService = meService;
    }

    /// <summary>Cadastro do colaborador ligado à conta; 404 se a conta não tiver vínculo.</summary>
    [HttpGet("cadastro")]
    [ProducesResponseType(typeof(FuncionarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FuncionarioDto>> ObterCadastro()
    {
        return Ok(await _meService.ObterCadastroAsync(this.UsuarioIdAutenticado()));
    }

    /// <summary>Histórico de remuneração (salário, vale e promoções), mais recentes primeiro.</summary>
    [HttpGet("ajustes-remuneracao")]
    [ProducesResponseType(typeof(List<AjusteRemuneracaoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AjusteRemuneracaoDto>>> ListarAjustesRemuneracao()
    {
        return Ok(await _meService.ListarAjustesRemuneracaoAsync(this.UsuarioIdAutenticado()));
    }

    /// <summary>Holerites, da competência mais recente pra mais antiga.</summary>
    [HttpGet("holerites")]
    [ProducesResponseType(typeof(List<FolhaPagamentoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FolhaPagamentoDto>>> ListarHolerites()
    {
        return Ok(await _meService.ListarHoleritesAsync(this.UsuarioIdAutenticado()));
    }
}
