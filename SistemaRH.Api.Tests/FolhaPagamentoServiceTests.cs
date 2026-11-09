using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class FolhaPagamentoServiceTests
{
    private static FolhaPagamentoService CriarServico(out AppDbContext db)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        return new FolhaPagamentoService(db);
    }

    private static int CriarFuncionario(AppDbContext db)
    {
        var departamento = new Departamento { Nome = "Financeiro", Descricao = "Financeiro" };
        var cargo = new Cargo { Nome = "Analista Financeiro", SalarioBase = 4000m, Descricao = "Financeiro" };
        db.Departamentos.Add(departamento);
        db.Cargos.Add(cargo);
        db.SaveChanges();

        var funcionario = new Funcionario
        {
            Nome = "Mariana Santos",
            Cpf = "444.444.444-44",
            DataAdmissao = new DateTime(2024, 1, 1),
            DepartamentoId = departamento.Id,
            CargoId = cargo.Id,
        };
        db.Funcionarios.Add(funcionario);
        db.SaveChanges();

        return funcionario.Id;
    }

    [Fact]
    public async Task Criar_ComSalarioBonusEDescontos_CalculaSalarioLiquidoCorretamente()
    {
        var service = CriarServico(out var db);
        var funcionarioId = CriarFuncionario(db);

        var folha = await service.CriarAsync(funcionarioId, new FolhaPagamentoCreateDto
        {
            Competencia = "2026-08",
            Salario = 4000m,
            Bonus = 400m,
            Descontos = 300m,
        });

        Assert.Equal(4100m, folha.SalarioLiquido);
    }

    [Fact]
    public async Task Criar_ComCompetenciaDuplicadaParaMesmoFuncionario_LancaConflictException()
    {
        var service = CriarServico(out var db);
        var funcionarioId = CriarFuncionario(db);
        await service.CriarAsync(funcionarioId, new FolhaPagamentoCreateDto { Competencia = "2026-08", Salario = 4000m });

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CriarAsync(funcionarioId, new FolhaPagamentoCreateDto { Competencia = "2026-08", Salario = 4000m }));
    }

    [Fact]
    public async Task Criar_ParaFuncionarioInexistente_LancaNotFoundException()
    {
        var service = CriarServico(out _);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CriarAsync(999, new FolhaPagamentoCreateDto { Competencia = "2026-08", Salario = 4000m }));
    }

    [Fact]
    public async Task ListarPorFuncionario_DevolveOrdenadoPorCompetenciaDecrescente()
    {
        var service = CriarServico(out var db);
        var funcionarioId = CriarFuncionario(db);
        await service.CriarAsync(funcionarioId, new FolhaPagamentoCreateDto { Competencia = "2026-07", Salario = 4000m });
        await service.CriarAsync(funcionarioId, new FolhaPagamentoCreateDto { Competencia = "2026-08", Salario = 4000m });

        var folhas = await service.ListarPorFuncionarioAsync(funcionarioId);

        Assert.Equal("2026-08", folhas[0].Competencia);
        Assert.Equal("2026-07", folhas[1].Competencia);
    }
}
