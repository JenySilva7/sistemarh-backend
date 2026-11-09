using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class PontoServiceTests
{
    private static PontoService CriarServico(out Data.AppDbContext db)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        return new PontoService(db);
    }

    [Fact]
    public async Task RegistrarProximo_PrimeiraVezNoDia_RegistraEntrada()
    {
        var service = CriarServico(out _);

        var resultado = await service.RegistrarProximoAsync(usuarioId: 1);

        Assert.Equal(TipoRegistroPonto.Entrada, resultado.Registro.Tipo);
        Assert.Equal(TipoRegistroPonto.SaidaAlmoco, resultado.Proximo);
    }

    [Fact]
    public async Task RegistrarProximo_ChamadoQuatroVezes_SeguemAOrdemCorreta()
    {
        var service = CriarServico(out _);

        var r1 = await service.RegistrarProximoAsync(1);
        var r2 = await service.RegistrarProximoAsync(1);
        var r3 = await service.RegistrarProximoAsync(1);
        var r4 = await service.RegistrarProximoAsync(1);

        Assert.Equal(TipoRegistroPonto.Entrada, r1.Registro.Tipo);
        Assert.Equal(TipoRegistroPonto.SaidaAlmoco, r2.Registro.Tipo);
        Assert.Equal(TipoRegistroPonto.VoltaAlmoco, r3.Registro.Tipo);
        Assert.Equal(TipoRegistroPonto.Saida, r4.Registro.Tipo);
        Assert.Null(r4.Proximo); // jornada concluída, não tem próximo
    }

    [Fact]
    public async Task RegistrarProximo_QuandoJornadaJaConcluida_LancaBusinessRuleException()
    {
        var service = CriarServico(out _);
        for (var i = 0; i < 4; i++)
        {
            await service.RegistrarProximoAsync(1);
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegistrarProximoAsync(1));
    }

    [Fact]
    public async Task ListarRegistrosDeHoje_SoDevolveDoUsuarioCerto()
    {
        var service = CriarServico(out _);
        await service.RegistrarProximoAsync(usuarioId: 1);
        await service.RegistrarProximoAsync(usuarioId: 2);

        var registrosUsuario1 = await service.ListarRegistrosDeHojeAsync(1);

        var registro = Assert.Single(registrosUsuario1);
        Assert.Equal(TipoRegistroPonto.Entrada, registro.Tipo);
    }
}
