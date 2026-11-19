using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;
using SistemaRH.Api.Services;
using Xunit;

namespace SistemaRH.Api.Tests;

public class AusenciaServiceTests
{
    // A data da ausência vai de hoje − 365 até hoje + 365 dias.
    private static readonly DateTime DataValida = TestUtils.Hoje.AddDays(-8);

    private static AusenciaCreateDto CriarDtoValido() => new()
    {
        Tipo = TipoAusencia.AtestadoMedico,
        Data = DataValida,
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
    public async Task ObterPorId_DeOutroUsuario_Lanca404ComoSeNaoExistisse()
    {
        // 403 de um lado e 404 do outro revelavam quais ids existem.
        var service = CriarServico(out _);
        var criada = await service.CriarAsync(usuarioId: 1, CriarDtoValido());

        var erro = await Assert.ThrowsAsync<NotFoundException>(() => service.ObterPorIdAsync(usuarioId: 2, criada.Id));

        Assert.Equal("Justificativa de ausência não encontrada.", erro.Message);
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
        Assert.Equal(criada.Versao + 1, resultado.Versao);
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
    public async Task Cancelar_DeOutroUsuario_Lanca404()
    {
        var service = CriarServico(out _);
        var criada = await service.CriarAsync(usuarioId: 1, CriarDtoValido());

        await Assert.ThrowsAsync<NotFoundException>(() => service.CancelarAsync(usuarioId: 2, criada.Id));
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

    // ── Duplicata, enum, data e texto ───────────────────────────────────

    [Fact]
    public async Task Criar_MesmoTipoEMesmaDataAindaEmAberto_Lanca409()
    {
        var service = CriarServico(out var db);
        await service.CriarAsync(1, CriarDtoValido());

        var erro = await Assert.ThrowsAsync<ConflictException>(() => service.CriarAsync(1, CriarDtoValido()));

        Assert.Equal("Você já enviou uma justificativa desse tipo para essa data, e ela ainda está em análise.", erro.Message);
        Assert.Single(db.Ausencias);
    }

    [Fact]
    public async Task Criar_MesmoTipoEMesmaData_DepoisDeConcluida_EhPermitido()
    {
        var service = CriarServico(out var db);
        var primeira = await service.CriarAsync(1, CriarDtoValido());
        (await db.Ausencias.FindAsync(primeira.Id))!.Status = StatusAusencia.Recusada;
        await db.SaveChangesAsync();

        await service.CriarAsync(1, CriarDtoValido());
        var outroTipo = CriarDtoValido();
        outroTipo.Tipo = TipoAusencia.Outro;
        await service.CriarAsync(1, outroTipo);

        Assert.Equal(3, db.Ausencias.Count());
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public async Task Criar_ComTipoForaDaLista_Lanca400(int tipo)
    {
        var service = CriarServico(out var db);
        var dto = CriarDtoValido();
        dto.Tipo = (TipoAusencia)tipo;

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(1, dto));

        Assert.Equal("Tipo de justificativa inválido.", erro.Message);
        Assert.Empty(db.Ausencias);
    }

    [Theory]
    [InlineData(-366)]
    [InlineData(366)]
    public async Task Criar_ComDataForaDaFaixa_Lanca400(int dias)
    {
        var service = CriarServico(out _);
        var dto = CriarDtoValido();
        dto.Data = TestUtils.Hoje.AddDays(dias);

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(1, dto));

        Assert.StartsWith("A data da ausência precisa estar entre", erro.Message);
    }

    [Fact]
    public async Task Criar_SemDataOuSemTipo_Lanca400()
    {
        var service = CriarServico(out _);
        var semData = CriarDtoValido();
        semData.Data = null;
        var semTipo = CriarDtoValido();
        semTipo.Tipo = null;

        Assert.Equal("Informe a data da ausência.", (await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(1, semData))).Message);
        Assert.Equal("Selecione o tipo de justificativa.", (await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(1, semTipo))).Message);
    }

    [Theory]
    [InlineData("  a  ", "A descrição precisa ter pelo menos 3 caracteres.")]
    [InlineData("Texto\u200Bescondido", "A descrição contém caracteres invisíveis ou de controle, que não são permitidos.")]
    [InlineData("Com\ttab", "A descrição contém caracteres invisíveis ou de controle, que não são permitidos.")]
    public async Task Criar_ComDescricaoInvalida_Lanca400(string descricao, string mensagem)
    {
        var service = CriarServico(out _);
        var dto = CriarDtoValido();
        dto.Descricao = descricao;

        var erro = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CriarAsync(1, dto));

        Assert.Equal(mensagem, erro.Message);
    }

    [Fact]
    public async Task Criar_DescricaoComQuebrasDeLinha_NormalizaEMantem()
    {
        var service = CriarServico(out _);
        var dto = CriarDtoValido();
        dto.Descricao = "\r\n  Consulta  marcada\r\n\r\n\r\n\r\nàs 10h\rcom atestado  \n";

        var criada = await service.CriarAsync(1, dto);

        Assert.Equal("Consulta marcada\n\nàs 10h\ncom atestado", criada.Descricao);
    }

    [Fact]
    public async Task Atualizar_ComVersaoDesatualizada_Lanca409()
    {
        var service = CriarServico(out _);
        var criada = await service.CriarAsync(1, CriarDtoValido());
        var dto = CriarDtoValido();
        dto.Descricao = "Primeira edição";
        await service.AtualizarAsync(1, criada.Id, dto);

        dto.Descricao = "Edição de uma tela velha";
        dto.Versao = criada.Versao;
        var erro = await Assert.ThrowsAsync<ConflictException>(() => service.AtualizarAsync(1, criada.Id, dto));

        Assert.Equal("Este registro foi alterado por outra pessoa. Atualize a tela e tente de novo.", erro.Message);
        Assert.Equal(HttpStatus409, erro.StatusCode);
    }

    [Fact]
    public async Task Cancelar_ComVersaoDesatualizada_Lanca409()
    {
        var service = CriarServico(out var db);
        var criada = await service.CriarAsync(1, CriarDtoValido());
        var dto = CriarDtoValido();
        dto.Descricao = "Editada em outra aba";
        await service.AtualizarAsync(1, criada.Id, dto);

        await Assert.ThrowsAsync<ConflictException>(() => service.CancelarAsync(1, criada.Id, versao: criada.Versao));

        Assert.Single(db.Ausencias);
    }

    [Fact]
    public async Task Cancelar_EnquantoORhDecide_OSegundoAGravarRecebeConflitoDeConcorrencia()
    {
        // Duas requisições ao mesmo tempo (dois contextos no mesmo banco): o
        // RH aprova e o colaborador cancela a partir da MESMA versão lida.
        var nomeDoBanco = Guid.NewGuid().ToString();
        var dbColaborador = TestUtils.CriarDbContextEmMemoria(nomeDoBanco);
        var colaborador = TestUtils.CriarUsuario(dbColaborador, "colaborador@empresa.com");
        var criada = await new AusenciaService(dbColaborador).CriarAsync(colaborador.Id, CriarDtoValido());
        var dbRh = TestUtils.CriarDbContextEmMemoria(nomeDoBanco);
        var lidaPeloColaborador = await dbColaborador.Ausencias.FindAsync(criada.Id);

        await new RhService(dbRh).AtualizarStatusAusenciaAsync(criada.Id, new AtualizarStatusAusenciaDto { Status = StatusAusencia.Aprovada });
        dbColaborador.Ausencias.Remove(lidaPeloColaborador!);

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>(() => dbColaborador.SaveChangesAsync());
    }

    private const int HttpStatus409 = 409;
}
