using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;

namespace SistemaRH.Api.Services;

public interface IMeService
{
    Task<FuncionarioDto> ObterCadastroAsync(int usuarioId);
    Task<List<AjusteRemuneracaoDto>> ListarAjustesRemuneracaoAsync(int usuarioId);
    Task<List<FolhaPagamentoDto>> ListarHoleritesAsync(int usuarioId);
}

/// <summary>
/// Dados do próprio usuário (/api/me): o cadastro de colaborador ligado à
/// conta, o histórico de remuneração e os holerites. Antes o "Meu Cadastro"
/// do painel e o Holerite do app mostravam valores inventados, porque não
/// havia rota pra pessoa ler os próprios dados sem ser RH.
/// O Id vem sempre do token, então ninguém lê os dados de outra pessoa.
/// </summary>
public class MeService : IMeService
{
    public const string MensagemSemVinculo = "Sua conta não está ligada a um cadastro de colaborador.";

    private readonly AppDbContext _db;
    private readonly IFuncionarioService _funcionarioService;
    private readonly IFolhaPagamentoService _folhaPagamentoService;

    public MeService(AppDbContext db, IFuncionarioService funcionarioService, IFolhaPagamentoService folhaPagamentoService)
    {
        _db = db;
        _funcionarioService = funcionarioService;
        _folhaPagamentoService = folhaPagamentoService;
    }

    public async Task<FuncionarioDto> ObterCadastroAsync(int usuarioId)
    {
        var funcionarioId = await FuncionarioVinculadoAsync(usuarioId)
            ?? throw new NotFoundException(MensagemSemVinculo);
        return await _funcionarioService.ObterPorIdAsync(funcionarioId);
    }

    // As listas devolvem vazio pra conta sem vínculo: as telas mostram o
    // estado "nada por aqui" em vez de um erro.

    public async Task<List<AjusteRemuneracaoDto>> ListarAjustesRemuneracaoAsync(int usuarioId)
    {
        var funcionarioId = await FuncionarioVinculadoAsync(usuarioId);
        return funcionarioId is int id
            ? await FuncionarioService.ListarAjustesSemConferirAsync(_db, id)
            : new List<AjusteRemuneracaoDto>();
    }

    public async Task<List<FolhaPagamentoDto>> ListarHoleritesAsync(int usuarioId)
    {
        var funcionarioId = await FuncionarioVinculadoAsync(usuarioId);
        return funcionarioId is int id
            ? await _folhaPagamentoService.ListarPorFuncionarioAsync(id)
            : new List<FolhaPagamentoDto>();
    }

    private Task<int?> FuncionarioVinculadoAsync(int usuarioId) =>
        _db.Usuarios.Where(u => u.Id == usuarioId).Select(u => u.FuncionarioId).FirstOrDefaultAsync();
}
