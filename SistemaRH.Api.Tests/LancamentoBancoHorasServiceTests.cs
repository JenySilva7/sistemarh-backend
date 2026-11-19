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

        // Conta ligada ao funcionário direto no banco, pra testar só o cálculo
        // do saldo (o vínculo pelo RH tem testes próprios).
        var usuario = TestUtils.CriarUsuario(db, "vinculado@empresa.com", funcionarioId: funcionarioId);
        usuario.SaldoBancoHorasMinutos = 9999; // deve ser ignorado, pois há FuncionarioId
        await db.SaveChangesAsync();
        var authService = new AuthService(db, new JwtTokenServiceFalso());

        var usuarioAtualizado = await authService.ObterUsuarioAsync(usuario.Id);

        Assert.Equal(200, usuarioAtualizado.SaldoBancoHorasMinutos);
    }

    [Fact]
    public async Task AuthService_UsuarioSemFuncionarioVinculado_UsaSaldoAchatado()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        // Conta antiga/demo, com o saldo "achatado" já gravado (o register
        // não aceita mais esse campo vindo do cliente).
        var usuario = TestUtils.CriarUsuario(db, "semfuncionario@empresa.com");
        usuario.SaldoBancoHorasMinutos = 450;
        await db.SaveChangesAsync();
        var authService = new AuthService(db, new JwtTokenServiceFalso());

        var resultado = await authService.LoginAsync(new Dtos.LoginDto { Email = "semfuncionario@empresa.com", Senha = "senha123" });

        Assert.Equal(450, resultado.Usuario.SaldoBancoHorasMinutos);
    }

    [Fact]
    public async Task Registrar_ComDataTipoOuMinutosInvalidos_LancaBusinessRule()
    {
        var service = CriarServico(out var db);
        var funcionarioId = CriarFuncionario(db);

        var semData = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegistrarAsync(funcionarioId,
            new LancamentoBancoHorasCreateDto { Tipo = TipoLancamentoBancoHoras.Credito, QuantidadeMinutos = 60 }));
        var tipoInvalido = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegistrarAsync(funcionarioId,
            new LancamentoBancoHorasCreateDto { DataRegistro = new DateTime(2026, 8, 10), Tipo = (TipoLancamentoBancoHoras)7, QuantidadeMinutos = 60 }));
        var antesDaAdmissao = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegistrarAsync(funcionarioId,
            new LancamentoBancoHorasCreateDto { DataRegistro = new DateTime(2001, 1, 1), Tipo = TipoLancamentoBancoHoras.Credito, QuantidadeMinutos = 60 }));
        var minutosDemais = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegistrarAsync(funcionarioId,
            new LancamentoBancoHorasCreateDto { DataRegistro = new DateTime(2026, 8, 10), Tipo = TipoLancamentoBancoHoras.Credito, QuantidadeMinutos = int.MaxValue }));

        Assert.Equal("Informe a data do lançamento.", semData.Message);
        Assert.Equal("Tipo de lançamento inválido.", tipoInvalido.Message);
        Assert.StartsWith("A data do lançamento precisa estar entre 01/01/2023", antesDaAdmissao.Message);
        Assert.Equal("A quantidade de minutos precisa estar entre 1 e 100000.", minutosDemais.Message);
        Assert.Empty(db.LancamentosBancoHoras);
    }
}
