using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IAuthService
{
    Task<AuthResponseDto> RegistrarAsync(RegisterDto dto);
    Task<AuthResponseDto> LoginAsync(LoginDto dto);
    Task<UsuarioDto> ObterUsuarioAsync(int usuarioId);
}

/// <summary>
/// Concentra as regras de negócio do login/cadastro. O Controller só recebe
/// a requisição e chama esse serviço — toda a lógica "de verdade" mora aqui.
///
/// Sobre a senha: nunca comparamos "senha digitada == senha salva". Usamos o
/// <see cref="PasswordHasher{TUser}"/> do ASP.NET Core, que:
///   1. No cadastro, transforma a senha num hash (um "resumo" irreversível,
///      com um "sal" aleatório misturado) e salva só esse hash.
///   2. No login, pega a senha digitada, aplica o mesmo algoritmo de hash e
///      compara o resultado com o hash salvo — sem nunca descobrir a senha
///      original de volta. Mesmo se o banco vazar, ninguém recupera a senha.
/// </summary>
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly PasswordHasher<Usuario> _passwordHasher = new();

    public AuthService(AppDbContext db, IJwtTokenService jwtTokenService)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthResponseDto> RegistrarAsync(RegisterDto dto)
    {
        var emailJaExiste = await _db.Usuarios.AnyAsync(u => u.Email == dto.Email.Trim().ToLower());
        if (emailJaExiste)
        {
            throw new ConflictException("Já existe uma conta cadastrada com esse e-mail.");
        }

        var usuario = new Usuario
        {
            Nome = dto.Nome.Trim(),
            Email = dto.Email.Trim().ToLower(),
            Cargo = dto.Cargo.Trim(),
            Departamento = dto.Departamento.Trim(),
            SaldoBancoHorasMinutos = dto.SaldoBancoHorasMinutos,
            CriadoEm = DateTime.UtcNow,
        };
        // A senha ainda não existe no objeto — o hasher usa "usuario" só como
        // referência de tipo, então é seguro calcular o hash antes de ter Id.
        usuario.SenhaHash = _passwordHasher.HashPassword(usuario, dto.Senha);

        _db.Usuarios.Add(usuario);
        await _db.SaveChangesAsync();

        return new AuthResponseDto
        {
            Token = _jwtTokenService.GerarToken(usuario),
            Usuario = await ParaDtoAsync(usuario),
        };
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        var emailNormalizado = dto.Email.Trim().ToLower();
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == emailNormalizado);

        // De propósito, a mensagem de erro é igual tanto pra "usuário não existe"
        // quanto pra "senha errada". Se disséssemos "e-mail não encontrado",
        // alguém mal-intencionado conseguiria descobrir quais e-mails têm
        // conta só testando — isso se chama "user enumeration".
        const string mensagemErro = "E-mail ou senha incorretos.";

        if (usuario is null)
        {
            throw new BusinessRuleException(mensagemErro);
        }

        var resultado = _passwordHasher.VerifyHashedPassword(usuario, usuario.SenhaHash, dto.Senha);
        if (resultado == PasswordVerificationResult.Failed)
        {
            throw new BusinessRuleException(mensagemErro);
        }

        return new AuthResponseDto
        {
            Token = _jwtTokenService.GerarToken(usuario),
            Usuario = await ParaDtoAsync(usuario),
        };
    }

    public async Task<UsuarioDto> ObterUsuarioAsync(int usuarioId)
    {
        var usuario = await _db.Usuarios.FindAsync(usuarioId);
        if (usuario is null)
        {
            throw new NotFoundException("Usuário não encontrado.");
        }

        return await ParaDtoAsync(usuario);
    }

    /// <summary>
    /// Monta o UsuarioDto, unificando os dois "banco de horas" do projeto:
    /// se essa conta estiver ligada a um <see cref="Funcionario"/>
    /// (<see cref="Usuario.FuncionarioId"/> preenchido), o saldo vem do
    /// extrato de verdade (<see cref="LancamentoBancoHoras"/>, somando
    /// créditos e subtraindo débitos). Senão, cai no campo "achatado"
    /// <see cref="Usuario.SaldoBancoHorasMinutos"/> — é o caso de contas
    /// antigas/demo que nunca foram ligadas a um funcionário.
    /// </summary>
    private async Task<UsuarioDto> ParaDtoAsync(Usuario usuario)
    {
        var saldoMinutos = usuario.FuncionarioId is int funcionarioId
            ? await LancamentoBancoHorasService.CalcularSaldoAsync(_db, funcionarioId)
            : usuario.SaldoBancoHorasMinutos;

        var totalSolicitacoes = await _db.Ausencias.CountAsync(a => a.UsuarioId == usuario.Id);

        return new UsuarioDto
        {
            Id = usuario.Id,
            Nome = usuario.Nome,
            Email = usuario.Email,
            Cargo = usuario.Cargo,
            Departamento = usuario.Departamento,
            SaldoBancoHorasMinutos = saldoMinutos,
            TotalSolicitacoesAusencia = totalSolicitacoes,
        };
    }
}
