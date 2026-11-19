using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IMensagemRhService
{
    Task<MensagemRhDto> CriarAsync(int usuarioId, MensagemRhCreateDto dto);
    Task<List<MensagemRhDto>> ListarMinhasAsync(int usuarioId);
    Task<List<MensagemRhDto>> ListarAsync(StatusMensagemRh? status);
    Task<MensagemRhDto> ResponderAsync(int mensagemId, ResponderMensagemRhDto dto, int responsavelUsuarioId);
}

/// <summary>
/// "Falar com o RH": o colaborador envia pelo app, o RH lê e responde pelo
/// painel, e a resposta volta pro app em "Minhas mensagens".
/// </summary>
public class MensagemRhService : IMensagemRhService
{
    /// <summary>Janela em que a mesma mensagem, ainda aberta, conta como envio repetido.</summary>
    public static readonly TimeSpan JanelaDeRepeticao = TimeSpan.FromMinutes(2);

    public const string MensagemRepetida =
        "Você acabou de enviar essa mesma mensagem. Aguarde a resposta do RH.";

    private readonly AppDbContext _db;
    private readonly TimeProvider _relogio;

    public MensagemRhService(AppDbContext db, TimeProvider? relogio = null)
    {
        _db = db;
        _relogio = relogio ?? TimeProvider.System;
    }

    public async Task<MensagemRhDto> CriarAsync(int usuarioId, MensagemRhCreateDto dto)
    {
        // Trava a conta antes de ler: o mesmo envio repetido (duplo toque,
        // rede lenta, reenvio do cliente) chega junto e, sem isso, os dois
        // passavam na checagem de mensagem repetida.
        await using var transacao = await _db.IniciarTransacaoAsync();
        await _db.TravarUsuarioAsync(usuarioId);

        var usuario = await _db.Usuarios
            .Include(u => u.Funcionario)
            .FirstOrDefaultAsync(u => u.Id == usuarioId)
            ?? throw new NotFoundException("Usuário não encontrado.");

        // Só quem é colaborador (conta ligada a um cadastro ativo) fala com o
        // RH por aqui; contas avulsas não têm a quem o RH responder.
        if (usuario.Funcionario is null)
        {
            throw new ForbiddenException("Sua conta não está ligada a um cadastro de colaborador.");
        }
        if (usuario.Funcionario.Status != StatusFuncionario.Ativo)
        {
            throw new ForbiddenException(AuthService.MensagemAcessoDesativado);
        }

        var assunto = Textos.Validar(dto.Assunto, Textos.AssuntoMensagem);
        var corpo = Textos.Validar(dto.Mensagem, Textos.CorpoMensagem);

        // Mesma mensagem (assunto e texto já limpos), ainda aberta e enviada
        // há pouco: é reenvio, não uma mensagem nova pro RH responder.
        var agora = _relogio.GetUtcNow().UtcDateTime;
        var limite = agora - JanelaDeRepeticao;
        var repetida = await _db.MensagensRh.AnyAsync(m =>
            m.UsuarioId == usuarioId
            && m.Status == StatusMensagemRh.Aberta
            && m.Assunto == assunto
            && m.Mensagem == corpo
            && m.CriadoEm >= limite);
        if (repetida)
        {
            throw new ConflictException(MensagemRepetida);
        }

        var mensagem = new MensagemRh
        {
            UsuarioId = usuarioId,
            Usuario = usuario,
            Assunto = assunto,
            Mensagem = corpo,
            Status = StatusMensagemRh.Aberta,
            CriadoEm = agora,
        };
        _db.MensagensRh.Add(mensagem);
        await _db.SaveChangesAsync();
        await AppDbContext.ConfirmarAsync(transacao);

        return ParaDto(mensagem);
    }

    public async Task<List<MensagemRhDto>> ListarMinhasAsync(int usuarioId)
    {
        var mensagens = await ConsultaComNomes()
            .Where(m => m.UsuarioId == usuarioId)
            .OrderByDescending(m => m.CriadoEm)
            .ThenByDescending(m => m.Id)
            .ToListAsync();

        return mensagens.Select(ParaDto).ToList();
    }

    public async Task<List<MensagemRhDto>> ListarAsync(StatusMensagemRh? status)
    {
        Enums.ValidarFiltro(status, MensagensValidacao.MensagemStatusInvalido);

        var query = ConsultaComNomes();
        if (status is not null)
        {
            query = query.Where(m => m.Status == status);
        }

        var mensagens = await query.OrderByDescending(m => m.CriadoEm).ThenByDescending(m => m.Id).ToListAsync();
        return mensagens.Select(ParaDto).ToList();
    }

    public async Task<MensagemRhDto> ResponderAsync(int mensagemId, ResponderMensagemRhDto dto, int responsavelUsuarioId)
    {
        var mensagem = await ConsultaComNomes().FirstOrDefaultAsync(m => m.Id == mensagemId)
            ?? throw new NotFoundException("Mensagem não encontrada.");

        // Já respondida (por outra pessoa ou em outra aba) ou alterada desde
        // que a tela carregou: 409, pra ninguém sobrescrever a resposta de outro.
        Concorrencia.ConferirVersao(mensagem, dto.Versao);
        if (mensagem.Status == StatusMensagemRh.Respondida)
        {
            throw new ConflictException("Essa mensagem já foi respondida.");
        }

        var resposta = Textos.Validar(dto.Resposta, Textos.RespostaMensagem);
        var responsavel = await _db.Usuarios.FindAsync(responsavelUsuarioId);

        mensagem.Resposta = resposta;
        mensagem.Status = StatusMensagemRh.Respondida;
        mensagem.RespondidaEm = DateTime.UtcNow;
        mensagem.ResponsavelUsuarioId = responsavel?.Id;
        mensagem.ResponsavelUsuario = responsavel;

        await _db.SaveChangesAsync();
        return ParaDto(mensagem);
    }

    private IQueryable<MensagemRh> ConsultaComNomes() =>
        _db.MensagensRh
            .Include(m => m.Usuario)
            .Include(m => m.ResponsavelUsuario);

    private static MensagemRhDto ParaDto(MensagemRh mensagem) => new()
    {
        Id = mensagem.Id,
        UsuarioId = mensagem.UsuarioId,
        UsuarioNome = mensagem.Usuario?.Nome ?? string.Empty,
        Assunto = mensagem.Assunto,
        Mensagem = mensagem.Mensagem,
        Status = mensagem.Status,
        Resposta = mensagem.Resposta,
        RespondidaEm = mensagem.RespondidaEm,
        ResponsavelNome = mensagem.ResponsavelUsuario?.Nome,
        CriadoEm = mensagem.CriadoEm,
        Versao = mensagem.Versao,
    };
}
