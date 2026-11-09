using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class AusenciaServiceTests
{
    private static AusenciaCreateDto CriarDtoValido() => new()
    {
        Tipo = TipoAusencia.AtestadoMedico,
        Data = new DateTime(2026, 11, 10),
        DiaInteiro = true,
        Descricao = "Consulta médica de rotina",
    };

    private static AusenciaService CriarServico(out Data.AppDbContext db)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        return new AusenciaService(db);
    }

    [Fact]
    public async Task Criar_ComDadosValidos_SalvaComStatusEnviada()
    {
        var service = CriarServico(out _);

        var ausencia = await service.CriarAsync(usuarioId: 1, CriarDtoValido());

        Assert.Equal(StatusAusencia.Enviada, ausencia.Status);
        Assert.Equal("Consulta médica de rotina", ausencia.Descricao);
    }

    [Fact]
    public async Task Listar_SoDevolveAusenciasDoUsuarioCerto()
    {
        var service = CriarServico(out _);
        await service.CriarAsync(1, CriarDtoValido());
        await service.CriarAsync(2, CriarDtoValido());

        var ausenciasUsuario1 = await service.ListarAsync(1);

        Assert.Single(ausenciasUsuario1);
    }

    [Fact]
    public async Task ObterPorId_DeOutroUsuario_LancaForbiddenException()
    {
        var service = CriarServico(out _);
        var criada = await service.CriarAsync(usuarioId: 1, CriarDtoValido());

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ObterPorIdAsync(usuarioId: 2, criada.Id));
    }

    [Fact]
    public async Task ObterPorId_Inexistente_LancaNotFoundException()
    {
        var service = CriarServico(out _);

        await Assert.ThrowsAsync<NotFoundException>(() => service.ObterPorIdAsync(1, 999));
    }

    [Fact]
    public async Task Atualizar_EnquantoEnviada_FuncionaNormalmente()
    {
        var service = CriarServico(out _);
        var criada = await service.CriarAsync(1, CriarDtoValido());

        var dtoAtualizado = CriarDtoValido();
        dtoAtualizado.Descricao = "Descrição atualizada";
        var resultado = await service.AtualizarAsync(1, criada.Id, dtoAtualizado);

        Assert.Equal("Descrição atualizada", resultado.Descricao);
    }

    [Fact]
    public async Task Cancelar_EnquantoEnviada_RemoveORegistro()
    {
        var service = CriarServico(out _);
        var criada = await service.CriarAsync(1, CriarDtoValido());

        await service.CancelarAsync(1, criada.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => service.ObterPorIdAsync(1, criada.Id));
    }

    [Fact]
    public async Task Cancelar_DeOutroUsuario_LancaForbiddenException()
    {
        var service = CriarServico(out _);
        var criada = await service.CriarAsync(usuarioId: 1, CriarDtoValido());

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CancelarAsync(usuarioId: 2, criada.Id));
    }

    [Fact]
    public async Task Cancelar_QuandoJaEmAnalise_LancaBusinessRuleException()
    {
        var service = CriarServico(out var db);
        var criada = await service.CriarAsync(1, CriarDtoValido());

        // Simula o RH já tendo começado a analisar a solicitação.
        var entidade = await db.Ausencias.FindAsync(criada.Id);
        entidade!.Status = StatusAusencia.EmAnalise;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CancelarAsync(1, criada.Id));
    }
}
