using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class LancamentoBancoHorasServiceTests
{
    private static LancamentoBancoHorasService CriarServico(out AppDbContext db)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        return new LancamentoBancoHorasService(db);
    }

    private static int CriarFuncionario(AppDbContext db)
    {
        var departamento = new Departamento { Nome = "Tecnologia", Descricao = "TI" };
        var cargo = new Cargo { Nome = "Desenvolvedor", SalarioBase = 5000m, Descricao = "Dev" };
        db.Departamentos.Add(departamento);
        db.Cargos.Add(cargo);
        db.SaveChanges();

        var funcionario = new Funcionario
        {
            Nome = "Carlos Lima",
            Cpf = "333.333.333-33",
            DataAdmissao = new DateTime(2023, 1, 1),
            DepartamentoId = departamento.Id,
            CargoId = cargo.Id,
        };
        db.Funcionarios.Add(funcionario);
        db.SaveChanges();

        return funcionario.Id;
    }

    [Fact]
    public async Task ObterExtrato_ComCreditosEDebitos_CalculaSaldoCorretamente()
    {
        var service = CriarServico(out var db);
        var funcionarioId = CriarFuncionario(db);

        await service.RegistrarAsync(funcionarioId, new LancamentoBancoHorasCreateDto
        {
            DataRegistro = new DateTime(2026, 8, 10),
            Tipo = TipoLancamentoBancoHoras.Credito,
            QuantidadeMinutos = 120,
            Descricao = "Hora extra",
        });
        await service.RegistrarAsync(funcionarioId, new LancamentoBancoHorasCreateDto
        {
            DataRegistro = new DateTime(2026, 8, 12),
            Tipo = TipoLancamentoBancoHoras.Debito,
            QuantidadeMinutos = 30,
            Descricao = "Compensação",
        });

        var extrato = await service.ObterExtratoAsync(funcionarioId);

        Assert.Equal(90, extrato.SaldoMinutos);
        Assert.Equal(2, extrato.Lancamentos.Count);
    }

    [Fact]
    public async Task Registrar_ParaFuncionarioInexistente_LancaNotFoundException()
    {
        var service = CriarServico(out _);

        await Assert.ThrowsAsync<NotFoundException>(() => service.RegistrarAsync(999, new LancamentoBancoHorasCreateDto
        {
            DataRegistro = DateTime.UtcNow,
            Tipo = TipoLancamentoBancoHoras.Credito,
            QuantidadeMinutos = 60,
        }));
    }

    [Fact]
    public async Task CalcularSaldoAsync_SemLancamentos_DevolveZero()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var funcionarioId = CriarFuncionario(db);

        var saldo = await LancamentoBancoHorasService.CalcularSaldoAsync(db, funcionarioId);

        Assert.Equal(0, saldo);
    }

    /// <summary>
    /// Valida a unificação feita no AuthService: quando um Usuario tem
    /// FuncionarioId preenchido, o saldo devolvido no login/registro deve
    /// vir do extrato (LancamentoBancoHoras), não do campo achatado
    /// SaldoBancoHorasMinutos.
    /// </summary>
    [Fact]
    public async Task AuthService_UsuarioComFuncionarioVinculado_UsaSaldoDoExtrato()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var funcionarioId = CriarFuncionario(db);

        db.LancamentosBancoHoras.Add(new LancamentoBancoHoras
        {
            FuncionarioId = funcionarioId,
            DataRegistro = new DateTime(2026, 9, 1),
            Tipo = TipoLancamentoBancoHoras.Credito,
            QuantidadeMinutos = 200,
            Descricao = "Hora extra",
        });
        await db.SaveChangesAsync();

        var authService = new AuthService(db, new JwtTokenServiceFalso());
        var resultado = await authService.RegistrarAsync(new Dtos.RegisterDto
        {
            Nome = "Vinculado ao Funcionario",
            Email = "vinculado@empresa.com",
            Senha = "senha123",
            SaldoBancoHorasMinutos = 9999, // deve ser ignorado, pois há FuncionarioId
        });

        // Liga a conta recém-criada ao funcionário direto no banco, pra
        // testar só o cálculo do saldo (o vínculo pelo RH tem testes próprios).
        var usuario = await db.Usuarios.FindAsync(resultado.Usuario.Id);
        usuario!.FuncionarioId = funcionarioId;
        await db.SaveChangesAsync();

        var usuarioAtualizado = await authService.ObterUsuarioAsync(usuario.Id);

        Assert.Equal(200, usuarioAtualizado.SaldoBancoHorasMinutos);
    }

    [Fact]
    public async Task AuthService_UsuarioSemFuncionarioVinculado_UsaSaldoAchatado()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        var authService = new AuthService(db, new JwtTokenServiceFalso());

        var resultado = await authService.RegistrarAsync(new Dtos.RegisterDto
        {
            Nome = "Sem Funcionario",
            Email = "semfuncionario@empresa.com",
            Senha = "senha123",
            SaldoBancoHorasMinutos = 450,
        });

        Assert.Equal(450, resultado.Usuario.SaldoBancoHorasMinutos);
    }
}
