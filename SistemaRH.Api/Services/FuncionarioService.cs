using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IFuncionarioService
{
    Task<List<FuncionarioDto>> ListarAsync(StatusFuncionario? status, int? departamentoId);
    Task<FuncionarioDto> ObterPorIdAsync(int id);
    Task<FuncionarioDto> CriarAsync(FuncionarioCreateDto dto);
    Task<FuncionarioDto> AtualizarAsync(int id, FuncionarioUpdateDto dto);
    Task<FuncionarioDto> DesligarAsync(int id, DesligarFuncionarioDto dto);
}

/// <summary>
/// Cadastro completo de funcionários (CPF, cargo, departamento etc.) — o
/// lado administrativo/RH. Diferente de <see cref="AuthService"/>, que cuida
/// só da conta de login usada pelo app.
/// </summary>
public class FuncionarioService : IFuncionarioService
{
    private readonly AppDbContext _db;

    public FuncionarioService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<FuncionarioDto>> ListarAsync(StatusFuncionario? status, int? departamentoId)
    {
        var query = _db.Funcionarios
            .Include(f => f.Departamento)
            .Include(f => f.Cargo)
            .Include(f => f.Gerente)
            .AsQueryable();

        if (status is not null)
        {
            query = query.Where(f => f.Status == status);
        }
        if (departamentoId is not null)
        {
            query = query.Where(f => f.DepartamentoId == departamentoId);
        }

        var funcionarios = await query.OrderBy(f => f.Nome).ToListAsync();
        var mapaContagens = await ContarSolicitacoesPorFuncionarioAsync();
        return funcionarios.Select(f => ParaDto(f, mapaContagens.GetValueOrDefault(f.Id, 0))).ToList();
    }

    public async Task<FuncionarioDto> ObterPorIdAsync(int id)
    {
        var funcionario = await BuscarOuFalhar(id);
        var total = await ContarSolicitacoesAsync(id);
        return ParaDto(funcionario, total);
    }

    public async Task<FuncionarioDto> CriarAsync(FuncionarioCreateDto dto)
    {
        var cpfNormalizado = dto.Cpf.Trim();
        var cpfJaExiste = await _db.Funcionarios.AnyAsync(f => f.Cpf == cpfNormalizado);
        if (cpfJaExiste)
        {
            throw new ConflictException("Já existe um funcionário cadastrado com esse CPF.");
        }

        var emailNormalizado = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        if (emailNormalizado is not null && await _db.Funcionarios.AnyAsync(f => f.Email == emailNormalizado))
        {
            throw new ConflictException("Já existe um funcionário cadastrado com esse e-mail.");
        }

        await GarantirDepartamentoECargoExistemAsync(dto.DepartamentoId, dto.CargoId);
        await GarantirGerenteExisteAsync(dto.GerenteId);

        var funcionario = new Funcionario
        {
            Nome = dto.Nome.Trim(),
            Cpf = cpfNormalizado,
            Email = emailNormalizado,
            Telefone = string.IsNullOrWhiteSpace(dto.Telefone) ? null : dto.Telefone.Trim(),
            DataNascimento = dto.DataNascimento,
            DataAdmissao = dto.DataAdmissao,
            Status = StatusFuncionario.Ativo,
            DepartamentoId = dto.DepartamentoId,
            CargoId = dto.CargoId,
            GerenteId = dto.GerenteId,
            CriadoEm = DateTime.UtcNow,
        };

        _db.Funcionarios.Add(funcionario);
        await _db.SaveChangesAsync();

        return await ObterPorIdAsync(funcionario.Id);
    }

    public async Task<FuncionarioDto> AtualizarAsync(int id, FuncionarioUpdateDto dto)
    {
        var funcionario = await BuscarOuFalhar(id);
        await GarantirDepartamentoECargoExistemAsync(dto.DepartamentoId, dto.CargoId);

        if (dto.GerenteId == id)
        {
            throw new BusinessRuleException("Um funcionário não pode ser gestor de si mesmo.");
        }
        await GarantirGerenteExisteAsync(dto.GerenteId);

        var emailNormalizado = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        if (emailNormalizado is not null && await _db.Funcionarios.AnyAsync(f => f.Email == emailNormalizado && f.Id != id))
        {
            throw new ConflictException("Já existe um funcionário cadastrado com esse e-mail.");
        }

        funcionario.Nome = dto.Nome.Trim();
        funcionario.Email = emailNormalizado;
        funcionario.Telefone = string.IsNullOrWhiteSpace(dto.Telefone) ? null : dto.Telefone.Trim();
        funcionario.DataNascimento = dto.DataNascimento;
        funcionario.DepartamentoId = dto.DepartamentoId;
        funcionario.CargoId = dto.CargoId;
        funcionario.GerenteId = dto.GerenteId;

        await _db.SaveChangesAsync();
        return await ObterPorIdAsync(id);
    }

    public async Task<FuncionarioDto> DesligarAsync(int id, DesligarFuncionarioDto dto)
    {
        var funcionario = await BuscarOuFalhar(id);

        if (funcionario.Status == StatusFuncionario.Desligado)
        {
            throw new BusinessRuleException("Esse funcionário já está desligado.");
        }

        funcionario.Status = StatusFuncionario.Desligado;
        funcionario.DataDesligamento = dto.DataDesligamento ?? DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await ObterPorIdAsync(id);
    }

    private async Task GarantirDepartamentoECargoExistemAsync(int departamentoId, int cargoId)
    {
        var departamentoExiste = await _db.Departamentos.AnyAsync(d => d.Id == departamentoId);
        if (!departamentoExiste)
        {
            throw new NotFoundException("Departamento informado não existe.");
        }

        var cargoExiste = await _db.Cargos.AnyAsync(c => c.Id == cargoId);
        if (!cargoExiste)
        {
            throw new NotFoundException("Cargo informado não existe.");
        }
    }

    private async Task GarantirGerenteExisteAsync(int? gerenteId)
    {
        if (gerenteId is null) return;

        var gerenteExiste = await _db.Funcionarios.AnyAsync(f => f.Id == gerenteId);
        if (!gerenteExiste)
        {
            throw new NotFoundException("Gestor informado não existe.");
        }
    }

    /// <summary>Conta as ausências de qualquer Usuario vinculado a esse funcionário.</summary>
    private async Task<int> ContarSolicitacoesAsync(int funcionarioId)
    {
        return await _db.Usuarios
            .Where(u => u.FuncionarioId == funcionarioId)
            .SelectMany(u => u.Ausencias)
            .CountAsync();
    }

    /// <summary>Mesma contagem acima, mas pra todos os funcionários de uma vez (evita 1 query por linha ao listar).</summary>
    private async Task<Dictionary<int, int>> ContarSolicitacoesPorFuncionarioAsync()
    {
        var contagens = await _db.Usuarios
            .Where(u => u.FuncionarioId != null)
            .Select(u => new { FuncionarioId = u.FuncionarioId!.Value, Total = u.Ausencias.Count })
            .ToListAsync();

        return contagens
            .GroupBy(c => c.FuncionarioId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Total));
    }

    private async Task<Funcionario> BuscarOuFalhar(int id)
    {
        var funcionario = await _db.Funcionarios
            .Include(f => f.Departamento)
            .Include(f => f.Cargo)
            .Include(f => f.Gerente)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (funcionario is null)
        {
            throw new NotFoundException("Funcionário não encontrado.");
        }

        return funcionario;
    }

    private static FuncionarioDto ParaDto(Funcionario funcionario, int totalSolicitacoes) => new()
    {
        Id = funcionario.Id,
        Nome = funcionario.Nome,
        Cpf = funcionario.Cpf,
        Email = funcionario.Email,
        Telefone = funcionario.Telefone,
        DataNascimento = funcionario.DataNascimento,
        DataAdmissao = funcionario.DataAdmissao,
        DataDesligamento = funcionario.DataDesligamento,
        Status = funcionario.Status,
        DepartamentoId = funcionario.DepartamentoId,
        DepartamentoNome = funcionario.Departamento?.Nome ?? string.Empty,
        CargoId = funcionario.CargoId,
        CargoNome = funcionario.Cargo?.Nome ?? string.Empty,
        GerenteId = funcionario.GerenteId,
        GerenteNome = funcionario.Gerente?.Nome,
        TotalSolicitacoesAusencia = totalSolicitacoes,
    };
}
