using Microsoft.AspNetCore.Identity;
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
    Task<FuncionarioDto> DefinirAcessoAppAsync(int funcionarioId, string senha);
}

/// <summary>
/// Cadastro completo de funcionários (CPF, cargo, departamento etc.) — o
/// lado administrativo/RH. Diferente de <see cref="AuthService"/>, que cuida
/// só da conta de login usada pelo app.
/// </summary>
public class FuncionarioService : IFuncionarioService
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<Usuario> _passwordHasher = new();

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
        // O mapa só tem chave pra funcionário com conta vinculada — então
        // ContainsKey já responde se ele tem acesso ao app.
        return funcionarios
            .Select(f => ParaDto(f, mapaContagens.GetValueOrDefault(f.Id, 0), mapaContagens.ContainsKey(f.Id)))
            .ToList();
    }

    public async Task<FuncionarioDto> ObterPorIdAsync(int id)
    {
        var funcionario = await BuscarOuFalhar(id);
        var (total, possuiAcessoApp) = await ContarSolicitacoesAsync(id);
        return ParaDto(funcionario, total, possuiAcessoApp);
    }

    public async Task<FuncionarioDto> CriarAsync(FuncionarioCreateDto dto)
    {
        // Acesso ao app é opcional no cadastro. Se veio senha, valida antes de
        // gravar qualquer coisa, pra uma falha não deixar o funcionário criado pela metade.
        var criarAcessoApp = !string.IsNullOrEmpty(dto.SenhaAcessoApp);
        if (criarAcessoApp)
        {
            ValidarDadosAcessoApp(dto.Email, dto.SenhaAcessoApp);
        }

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

        if (criarAcessoApp)
        {
            await PrepararAcessoAppAsync(funcionario, dto.SenhaAcessoApp!);
        }

        // Um SaveChanges só: funcionário e acesso ao app são gravados juntos.
        _db.Funcionarios.Add(funcionario);
        await _db.SaveChangesAsync();

        return await ObterPorIdAsync(funcionario.Id);
    }

    public async Task<FuncionarioDto> DefinirAcessoAppAsync(int funcionarioId, string senha)
    {
        var funcionario = await BuscarOuFalhar(funcionarioId);

        if (funcionario.Status != StatusFuncionario.Ativo)
        {
            throw new BusinessRuleException("Não é possível dar acesso ao app a um colaborador desligado.");
        }
        ValidarDadosAcessoApp(funcionario.Email, senha);

        await PrepararAcessoAppAsync(funcionario, senha);
        await _db.SaveChangesAsync();

        return await ObterPorIdAsync(funcionarioId);
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

    private static void ValidarDadosAcessoApp(string? email, string? senha)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BusinessRuleException("Cadastre um e-mail para esse colaborador antes de criar o acesso ao app.");
        }
        if (senha is null || senha.Length < 6)
        {
            throw new BusinessRuleException("A senha de acesso ao app precisa ter pelo menos 6 caracteres.");
        }
    }

    /// <summary>
    /// Escolhe qual conta de login vira o acesso desse funcionário e define a
    /// senha — sem salvar (quem chama decide quando). Ordem:
    ///   1. conta já vinculada a ele → só troca a senha (serve de "redefinir senha");
    ///   2. conta com o mesmo e-mail → vincula e sobrescreve a senha, pra que
    ///      quem tenha se cadastrado sozinho com esse e-mail perca o acesso;
    ///   3. nenhuma → cria uma conta nova com perfil Funcionario.
    /// Vínculo só acontece por aqui (ação do RH), nunca no login/cadastro do
    /// app — sem verificação de e-mail, isso abriria brecha pra roubo de conta.
    /// </summary>
    private async Task PrepararAcessoAppAsync(Funcionario funcionario, string senha)
    {
        // Funcionário ainda não salvo (Id 0) não tem como já ter conta vinculada.
        var usuario = funcionario.Id == 0
            ? null
            : await _db.Usuarios.FirstOrDefaultAsync(u => u.FuncionarioId == funcionario.Id);

        if (usuario is null)
        {
            var emailNormalizado = funcionario.Email!.Trim().ToLower();
            usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == emailNormalizado);

            if (usuario is not null)
            {
                if (usuario.FuncionarioId is not null && usuario.FuncionarioId != funcionario.Id)
                {
                    throw new ConflictException("Esse e-mail já está vinculado ao acesso de outro colaborador.");
                }
                // Sem isso, um usuário de RH poderia redefinir a senha de outra
                // conta de RH/Admin só cadastrando um funcionário com o e-mail dela.
                if (usuario.Perfil != PerfilUsuario.Funcionario)
                {
                    throw new ConflictException("Esse e-mail pertence a uma conta de RH/Admin e não pode ser usado como acesso de colaborador.");
                }
                usuario.Funcionario = funcionario;
            }
            else
            {
                var cargo = funcionario.Cargo ?? await _db.Cargos.FindAsync(funcionario.CargoId);
                var departamento = funcionario.Departamento ?? await _db.Departamentos.FindAsync(funcionario.DepartamentoId);

                usuario = new Usuario
                {
                    Nome = funcionario.Nome,
                    Email = emailNormalizado,
                    Perfil = PerfilUsuario.Funcionario,
                    Cargo = cargo?.Nome ?? string.Empty,
                    Departamento = departamento?.Nome ?? string.Empty,
                    Funcionario = funcionario,
                    CriadoEm = DateTime.UtcNow,
                };
                _db.Usuarios.Add(usuario);
            }
        }

        usuario.SenhaHash = _passwordHasher.HashPassword(usuario, senha);
    }

    /// <summary>
    /// Conta as ausências de qualquer Usuario vinculado a esse funcionário, e
    /// já aproveita a mesma consulta pra saber se existe algum vinculado.
    /// </summary>
    private async Task<(int Total, bool PossuiAcessoApp)> ContarSolicitacoesAsync(int funcionarioId)
    {
        var contagensPorUsuario = await _db.Usuarios
            .Where(u => u.FuncionarioId == funcionarioId)
            .Select(u => u.Ausencias.Count)
            .ToListAsync();

        return (contagensPorUsuario.Sum(), contagensPorUsuario.Count > 0);
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

    private static FuncionarioDto ParaDto(Funcionario funcionario, int totalSolicitacoes, bool possuiAcessoApp) => new()
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
        PossuiAcessoApp = possuiAcessoApp,
    };
}
