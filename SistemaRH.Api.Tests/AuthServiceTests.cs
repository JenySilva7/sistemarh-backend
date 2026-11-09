using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

/// <summary>Dublê (fake) do serviço de JWT — os testes de AuthService não
/// precisam de um token de verdade, só que o método seja chamado.</summary>
public class JwtTokenServiceFalso : IJwtTokenService
{
    public string GerarToken(Usuario usuario) => $"token-falso-para-usuario-{usuario.Id}";
}

public class AuthServiceTests
{
    private static AuthService CriarServico(out Data.AppDbContext db)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        return new AuthService(db, new JwtTokenServiceFalso());
    }

    [Fact]
    public async Task Registrar_ComDadosValidos_CriaUsuarioEDevolveToken()
    {
        var service = CriarServico(out _);

        var resultado = await service.RegistrarAsync(new RegisterDto
        {
            Nome = "Ana Silva",
            Email = "ana@empresa.com",
            Senha = "senha123",
        });

        Assert.NotEmpty(resultado.Token);
        Assert.Equal("ana@empresa.com", resultado.Usuario.Email);
    }

    [Fact]
    public async Task Registrar_ComEmailJaExistente_LancaConflictException()
    {
        var service = CriarServico(out _);
        await service.RegistrarAsync(new RegisterDto { Nome = "Ana", Email = "ana@empresa.com", Senha = "senha123" });

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RegistrarAsync(new RegisterDto { Nome = "Outra Ana", Email = "ana@empresa.com", Senha = "outrasenha" }));
    }

    [Fact]
    public async Task Login_ComSenhaCorreta_DevolveToken()
    {
        var service = CriarServico(out _);
        await service.RegistrarAsync(new RegisterDto { Nome = "Ana", Email = "ana@empresa.com", Senha = "senha123" });

        var resultado = await service.LoginAsync(new LoginDto { Email = "ana@empresa.com", Senha = "senha123" });

        Assert.NotEmpty(resultado.Token);
    }

    [Fact]
    public async Task Login_ComSenhaIncorreta_LancaBusinessRuleException()
    {
        var service = CriarServico(out _);
        await service.RegistrarAsync(new RegisterDto { Nome = "Ana", Email = "ana@empresa.com", Senha = "senha123" });

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.LoginAsync(new LoginDto { Email = "ana@empresa.com", Senha = "senhaErrada" }));
    }

    [Fact]
    public async Task Login_ComUsuarioInexistente_LancaBusinessRuleException()
    {
        var service = CriarServico(out _);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.LoginAsync(new LoginDto { Email = "naoexiste@empresa.com", Senha = "qualquer" }));
    }

    [Fact]
    public async Task ObterUsuario_ComIdInexistente_LancaNotFoundException()
    {
        var service = CriarServico(out _);

        await Assert.ThrowsAsync<NotFoundException>(() => service.ObterUsuarioAsync(999));
    }
}
