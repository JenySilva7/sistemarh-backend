using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class PontoServiceTests
{
    // 13/11/2026 09:00 em Brasília: longe da virada do dia, pra os testes
    // poderem "esperar" alguns minutos sem mudar de dia.
    private static readonly DateTimeOffset InicioDoExpediente = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static PontoService CriarServico(out Data.AppDbContext db, out RelogioFalso relogio)
    {
        db = TestUtils.CriarDbContextEmMemoria();
        relogio = new RelogioFalso(InicioDoExpediente);
        return new PontoService(db, relogio);
    }

    [Fact]
    public async Task RegistrarProximo_PrimeiraVezNoDia_RegistraEntrada()
    {
        var service = CriarServico(out _, out _);

        var resultado = await service.RegistrarProximoAsync(usuarioId: 1);

        Assert.Equal(TipoRegistroPonto.Entrada, resultado.Registro.Tipo);
        Assert.Equal(TipoRegistroPonto.SaidaAlmoco, resultado.Proximo);
    }

    [Fact]
    public async Task RegistrarProximo_ChamadoQuatroVezes_SeguemAOrdemCorreta()
    {
        var service = CriarServico(out _, out var relogio);

        var r1 = await service.RegistrarProximoAsync(1);
        relogio.Avancar(TimeSpan.FromHours(3));
        var r2 = await service.RegistrarProximoAsync(1);
        relogio.Avancar(TimeSpan.FromHours(1));
        var r3 = await service.RegistrarProximoAsync(1);
        relogio.Avancar(TimeSpan.FromHours(4));
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
        var service = CriarServico(out _, out var relogio);
        for (var i = 0; i < 4; i++)
        {
            await service.RegistrarProximoAsync(1);
            relogio.Avancar(TimeSpan.FromMinutes(5));
        }

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegistrarProximoAsync(1));

        Assert.Equal("Todas as marcações de ponto de hoje já foram registradas.", erro.Message);
    }

    [Fact]
    public async Task RegistrarProximo_MenosDeUmMinutoDepoisDoAnterior_Lanca400ENaoGrava()
    {
        // Antes, 4 toques seguidos fechavam o dia inteiro em 70 ms.
        var service = CriarServico(out var db, out var relogio);
        await service.RegistrarProximoAsync(1);
        relogio.Avancar(TimeSpan.FromSeconds(59));

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegistrarProximoAsync(1));

        Assert.Equal("Aguarde 1 minuto entre registros de ponto.", erro.Message);
        Assert.Single(db.RegistrosPonto);

        relogio.Avancar(TimeSpan.FromSeconds(1));
        var segunda = await service.RegistrarProximoAsync(1);
        Assert.Equal(TipoRegistroPonto.SaidaAlmoco, segunda.Registro.Tipo);
    }

    [Fact]
    public async Task RegistrarProximo_IntervaloEhPorUsuario()
    {
        var service = CriarServico(out _, out _);
        await service.RegistrarProximoAsync(1);

        var outroUsuario = await service.RegistrarProximoAsync(2);

        Assert.Equal(TipoRegistroPonto.Entrada, outroUsuario.Registro.Tipo);
    }

    [Fact]
    public async Task ListarRegistrosDeHoje_SoDevolveDoUsuarioCerto()
    {
        var service = CriarServico(out _, out _);
        await service.RegistrarProximoAsync(usuarioId: 1);
        await service.RegistrarProximoAsync(usuarioId: 2);

        var registrosUsuario1 = await service.ListarRegistrosDeHojeAsync(1);

        var registro = Assert.Single(registrosUsuario1);
        Assert.Equal(TipoRegistroPonto.Entrada, registro.Tipo);
    }
}
