using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;

namespace SistemaRH.Api.Controllers;

/// <summary>
/// Cadastro de funcionários (lado administrativo/RH). Todas as rotas exigem
/// perfil RH ou Admin — diferente das rotas do app (/api/ponto,
/// /api/ausencias), que qualquer colaborador autenticado usa pra si mesmo.
/// </summary>
[ApiController]
[Route("api/funcionarios")]
[Authorize(Policy = "RhOuAdmin")]
public class FuncionariosController : ControllerBase
{
    private readonly IFuncionarioService _funcionarioService;
    private readonly ILancamentoBancoHorasService _bancoHorasService;

    public FuncionariosController(IFuncionarioService funcionarioService, ILancamentoBancoHorasService bancoHorasService)
    {
        _funcionarioService = funcionarioService;
        _bancoHorasService = bancoHorasService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<FuncionarioDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FuncionarioDto>>> Listar([FromQuery] StatusFuncionario? status, [FromQuery] int? departamentoId)
    {
        return Ok(await _funcionarioService.ListarAsync(status, departamentoId));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(FuncionarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FuncionarioDto>> ObterPorId(int id)
    {
        return Ok(await _funcionarioService.ObterPorIdAsync(id));
    }

    [HttpPost]
    [ProducesResponseType(typeof(FuncionarioDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FuncionarioDto>> Criar(FuncionarioCreateDto dto)
    {
        var funcionario = await _funcionarioService.CriarAsync(dto);
        return CreatedAtAction(nameof(ObterPorId), new { id = funcionario.Id }, funcionario);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(FuncionarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FuncionarioDto>> Atualizar(int id, FuncionarioUpdateDto dto)
    {
        return Ok(await _funcionarioService.AtualizarAsync(id, dto));
    }

    /// <summary>Encerra o vínculo do funcionário (Status -> Desligado).</summary>
    [HttpPost("{id:int}/desligar")]
    [ProducesResponseType(typeof(FuncionarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FuncionarioDto>> Desligar(int id, DesligarFuncionarioDto dto)
    {
        return Ok(await _funcionarioService.DesligarAsync(id, dto));
    }

    /// <summary>Extrato completo do banco de horas do funcionário (saldo + lançamentos).</summary>
    [HttpGet("{id:int}/banco-horas")]
    [ProducesResponseType(typeof(ExtratoBancoHorasDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExtratoBancoHorasDto>> ObterExtratoBancoHoras(int id)
    {
        return Ok(await _bancoHorasService.ObterExtratoAsync(id));
    }

    /// <summary>Registra um novo lançamento (crédito ou débito) no banco de horas do funcionário.</summary>
    [HttpPost("{id:int}/banco-horas")]
    [ProducesResponseType(typeof(LancamentoBancoHorasDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LancamentoBancoHorasDto>> RegistrarLancamentoBancoHoras(int id, LancamentoBancoHorasCreateDto dto)
    {
        var lancamento = await _bancoHorasService.RegistrarAsync(id, dto);
        return CreatedAtAction(nameof(ObterExtratoBancoHoras), new { id }, lancamento);
    }
}
