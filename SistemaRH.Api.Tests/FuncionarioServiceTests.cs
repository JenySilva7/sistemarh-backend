using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class FuncionarioServiceTests
{
    private static FuncionarioService CriarServico(out AppDbContext db)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        return new FuncionarioService(db);
    }

    /// <summary>Cria um Departamento e um Cargo direto no banco de teste, pra satisfazer as FKs obrigatórias de Funcionario.</summary>
    private static (int departamentoId, int cargoId) CriarDepartamentoECargo(AppDbContext db)
    {
        var departamento = new Departamento { Nome = "Tecnologia", Descricao = "TI" };
        var cargo = new Cargo { Nome = "Desenvolvedor", SalarioBase = 5000m, Descricao = "Dev" };
        db.Departamentos.Add(departamento);
        db.Cargos.Add(cargo);
        db.SaveChanges();
        return (departamento.Id, cargo.Id);
    }

    private static FuncionarioCreateDto CriarDtoValido(int departamentoId, int cargoId, string cpf = "111.111.111-11") => new()
    {
        Nome = "Ana Souza",
        Cpf = cpf,
        Email = "ana.souza@empresa.com",
        DataAdmissao = new DateTime(2024, 1, 1),
        DepartamentoId = departamentoId,
        CargoId = cargoId,
    };

    [Fact]
    public async Task Criar_ComDadosValidos_SalvaComStatusAtivo()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);

        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        Assert.Equal(StatusFuncionario.Ativo, funcionario.Status);
        Assert.Equal("Tecnologia", funcionario.DepartamentoNome);
        Assert.Equal("Desenvolvedor", funcionario.CargoNome);
    }

    [Fact]
    public async Task Criar_ComCpfDuplicado_LancaConflictException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        await service.CriarAsync(CriarDtoValido(departamentoId, cargoId, cpf: "222.222.222-22"));

        var dtoDuplicado = CriarDtoValido(departamentoId, cargoId, cpf: "222.222.222-22");
        dtoDuplicado.Email = "outro.email@empresa.com";

        await Assert.ThrowsAsync<ConflictException>(() => service.CriarAsync(dtoDuplicado));
    }

    [Fact]
    public async Task Criar_ComDepartamentoInexistente_LancaNotFoundException()
    {
        var service = CriarServico(out var db);
        var (_, cargoId) = CriarDepartamentoECargo(db);

        var dto = CriarDtoValido(departamentoId: 999, cargoId);

        await Assert.ThrowsAsync<NotFoundException>(() => service.CriarAsync(dto));
    }

    [Fact]
    public async Task Desligar_FuncionarioAtivo_AtualizaStatusEData()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));

        var desligado = await service.DesligarAsync(funcionario.Id, new DesligarFuncionarioDto());

        Assert.Equal(StatusFuncionario.Desligado, desligado.Status);
        Assert.NotNull(desligado.DataDesligamento);
    }

    [Fact]
    public async Task Desligar_FuncionarioJaDesligado_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var (departamentoId, cargoId) = CriarDepartamentoECargo(db);
        var funcionario = await service.CriarAsync(CriarDtoValido(departamentoId, cargoId));
        await service.DesligarAsync(funcionario.Id, new DesligarFuncionarioDto());

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.DesligarAsync(funcionario.Id, new DesligarFuncionarioDto()));
    }
}
