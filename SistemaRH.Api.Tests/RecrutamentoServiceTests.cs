using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class RecrutamentoServiceTests
{
    private static RecrutamentoService CriarServico()
    {
        var db = TestUtils.CriarDbContextEmMemoria();
        return new RecrutamentoService(db);
    }

    private static RecrutamentoCreateDto CriarDtoValido() => new()
    {
        NomeCandidato = "Lucas Almeida",
        Email = "lucas@email.com",
        Vaga = "Desenvolvedor",
        DataCandidatura = new DateTime(2026, 8, 1),
        Observacoes = "Currículo em análise",
    };

    [Fact]
    public async Task Criar_ComDadosValidos_SalvaComStatusEmProcesso()
    {
        var service = CriarServico();

        var candidato = await service.CriarAsync(CriarDtoValido());

        Assert.Equal(StatusRecrutamento.EmProcesso, candidato.Status);
    }

    [Fact]
    public async Task AtualizarStatus_ParaAprovado_AtualizaCorretamente()
    {
        var service = CriarServico();
        var candidato = await service.CriarAsync(CriarDtoValido());

        var atualizado = await service.AtualizarStatusAsync(candidato.Id, new AtualizarStatusRecrutamentoDto
        {
            Status = StatusRecrutamento.Aprovado,
            Observacoes = "Aprovado no processo seletivo",
        });

        Assert.Equal(StatusRecrutamento.Aprovado, atualizado.Status);
        Assert.Equal("Aprovado no processo seletivo", atualizado.Observacoes);
    }

    [Fact]
    public async Task AtualizarStatus_Inexistente_LancaNotFoundException()
    {
        var service = CriarServico();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AtualizarStatusAsync(999, new AtualizarStatusRecrutamentoDto { Status = StatusRecrutamento.Rejeitado }));
    }

    [Fact]
    public async Task Listar_ComFiltroDeStatus_SoDevolveOsCorrespondentes()
    {
        var service = CriarServico();
        var candidato1 = await service.CriarAsync(CriarDtoValido());
        await service.AtualizarStatusAsync(candidato1.Id, new AtualizarStatusRecrutamentoDto { Status = StatusRecrutamento.Aprovado });
        await service.CriarAsync(CriarDtoValido());

        var aprovados = await service.ListarAsync(StatusRecrutamento.Aprovado);

        Assert.Single(aprovados);
    }
}
