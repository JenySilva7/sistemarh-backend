using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IAusenciaService
{
    Task<List<AusenciaDto>> ListarAsync(int usuarioId);
    Task<AusenciaDto> ObterPorIdAsync(int usuarioId, int ausenciaId);
    Task<AusenciaDto> CriarAsync(int usuarioId, AusenciaCreateDto dto);
    Task<AusenciaDto> AtualizarAsync(int usuarioId, int ausenciaId, AusenciaCreateDto dto, int? versao = null);
    Task CancelarAsync(int usuarioId, int ausenciaId, int? versao = null);
}

/// <summary>
/// CRUD de justificativas de ausência. Toda operação de leitura/escrita é
/// restrita ao dono do registro — mesmo que alguém descubra o Id de uma
/// ausência de outra pessoa, não consegue ver nem alterar (ver
/// <see cref="BuscarDoUsuarioOuFalhar"/>).
///
/// Edição e cancelamento só são permitidos enquanto o status ainda é
/// "Enviada": depois que o RH começa a analisar, a solicitação já não é mais
/// alterável pelo colaborador (regra de negócio real de qualquer fluxo de
/// aprovação). A <c>versao</c> garante que o colaborador não edite um texto
/// que o RH acabou de aprovar (quem chega depois recebe 409).
/// </summary>
public class AusenciaService : IAusenciaService
{
    private const string MensagemDuplicada =
        "Você já enviou uma justificativa desse tipo para essa data, e ela ainda está em análise.";

    private readonly AppDbContext _db;

    public AusenciaService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<AusenciaDto>> ListarAsync(int usuarioId)
    {
        var ausencias = await _db.Ausencias
            .Where(a => a.UsuarioId == usuarioId)
            .OrderByDescending(a => a.CriadoEm)
            .ThenByDescending(a => a.Id)
            .ToListAsync();

        return ausencias.Select(ParaDto).ToList();
    }

    public async Task<AusenciaDto> ObterPorIdAsync(int usuarioId, int ausenciaId)
    {
        var ausencia = await BuscarDoUsuarioOuFalhar(usuarioId, ausenciaId);
        return ParaDto(ausencia);
    }

    public async Task<AusenciaDto> CriarAsync(int usuarioId, AusenciaCreateDto dto)
    {
        var (tipo, data, descricao) = Validar(dto);

        // Trava a conta: o mesmo envio repetido (duplo toque, rede lenta)
        // chega junto e, sem isso, os dois passavam na checagem de duplicata.
        await using var transacao = await _db.IniciarTransacaoAsync();
        await _db.TravarUsuarioAsync(usuarioId);
        await GarantirQueNaoEhDuplicadaAsync(usuarioId, tipo, data, ausenciaIdAtual: null);

        var ausencia = new Ausencia
        {
            UsuarioId = usuarioId,
            Tipo = tipo,
            Data = data,
            DiaInteiro = dto.DiaInteiro,
            Descricao = descricao,
            Status = StatusAusencia.Enviada,
            CriadoEm = DateTime.UtcNow,
        };

        _db.Ausencias.Add(ausencia);
        await _db.SaveChangesAsync();
        await AppDbContext.ConfirmarAsync(transacao);

        return ParaDto(ausencia);
    }

    public async Task<AusenciaDto> AtualizarAsync(int usuarioId, int ausenciaId, AusenciaCreateDto dto, int? versao = null)
    {
        await using var transacao = await _db.IniciarTransacaoAsync();
        await _db.TravarUsuarioAsync(usuarioId);

        var ausencia = await BuscarDoUsuarioOuFalhar(usuarioId, ausenciaId);
        Concorrencia.ConferirVersao(ausencia, dto.Versao ?? versao);

        if (ausencia.Status != StatusAusencia.Enviada)
        {
            throw new BusinessRuleException(
                "Essa justificativa já está em análise (ou foi concluída) e não pode mais ser editada.");
        }

        var (tipo, data, descricao) = Validar(dto);
        await GarantirQueNaoEhDuplicadaAsync(usuarioId, tipo, data, ausenciaId);

        ausencia.Tipo = tipo;
        ausencia.Data = data;
        ausencia.DiaInteiro = dto.DiaInteiro;
        ausencia.Descricao = descricao;

        await _db.SaveChangesAsync();
        await AppDbContext.ConfirmarAsync(transacao);
        return ParaDto(ausencia);
    }

    public async Task CancelarAsync(int usuarioId, int ausenciaId, int? versao = null)
    {
        var ausencia = await BuscarDoUsuarioOuFalhar(usuarioId, ausenciaId);
        Concorrencia.ConferirVersao(ausencia, versao);

        if (ausencia.Status != StatusAusencia.Enviada)
        {
            throw new BusinessRuleException(
                "Essa justificativa já está em análise (ou foi concluída) e não pode mais ser cancelada.");
        }

        // O DELETE também confere a versão: se o RH decidiu nesse meio-tempo,
        // o cancelamento falha com 409 em vez de apagar uma decisão.
        _db.Ausencias.Remove(ausencia);
        await _db.SaveChangesAsync();
    }

    /// <summary>Tipo da lista, data entre hoje − 365 e hoje + 365 dias, descrição de 3 a 2000 caracteres.</summary>
    private static (TipoAusencia Tipo, DateTime Data, string Descricao) Validar(AusenciaCreateDto dto)
    {
        var tipo = Enums.Validar(dto.Tipo, "Selecione o tipo de justificativa.", "Tipo de justificativa inválido.");
        var hoje = Datas.Hoje();
        var data = Datas.ValidarFaixa(dto.Data, "Informe a data da ausência.", "A data da ausência",
            hoje.AddDays(-365), hoje.AddDays(365));
        var descricao = Textos.Validar(dto.Descricao, Textos.DescricaoAusencia);
        return (tipo, data, descricao);
    }

    /// <summary>Mesmo tipo e mesma data, ainda Enviada ou Em análise: é envio repetido.</summary>
    private async Task GarantirQueNaoEhDuplicadaAsync(int usuarioId, TipoAusencia tipo, DateTime data, int? ausenciaIdAtual)
    {
        var duplicada = await _db.Ausencias.AnyAsync(a =>
            a.UsuarioId == usuarioId
            && a.Tipo == tipo
            && a.Data == data
            && (a.Status == StatusAusencia.Enviada || a.Status == StatusAusencia.EmAnalise)
            && a.Id != ausenciaIdAtual);
        if (duplicada)
        {
            throw new ConflictException(MensagemDuplicada);
        }
    }

    private async Task<Ausencia> BuscarDoUsuarioOuFalhar(int usuarioId, int ausenciaId)
    {
        // "Não existe" e "não é sua" dão a mesma resposta (404): antes o
        // 403 de um lado e o 404 do outro revelavam quais ids existiam.
        var ausencia = await _db.Ausencias.FirstOrDefaultAsync(a => a.Id == ausenciaId && a.UsuarioId == usuarioId);
        if (ausencia is null)
        {
            throw new NotFoundException("Justificativa de ausência não encontrada.");
        }

        return ausencia;
    }

    private static AusenciaDto ParaDto(Ausencia ausencia) => new()
    {
        Id = ausencia.Id,
        Tipo = ausencia.Tipo,
        Data = ausencia.Data,
        DiaInteiro = ausencia.DiaInteiro,
        Descricao = ausencia.Descricao,
        Status = ausencia.Status,
        CriadoEm = ausencia.CriadoEm,
        Versao = ausencia.Versao,
    };
}
