using SistemaRH.Api.Common;

namespace SistemaRH.Api.Models;

public enum StatusMensagemRh
{
    Aberta = 0,
    Respondida = 1,
}

/// <summary>
/// Mensagem que o colaborador manda ao RH pelo app ("Falar com o RH"). Antes
/// a tela do app só mostrava "Mensagem enviada" sem chamar nada; agora a
/// mensagem fica gravada, aparece no painel do RH e a resposta volta pro app.
/// </summary>
public class MensagemRh : IVersionado
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public string Assunto { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;
    public StatusMensagemRh Status { get; set; } = StatusMensagemRh.Aberta;

    public string? Resposta { get; set; }
    public DateTime? RespondidaEm { get; set; }

    /// <summary>Conta de RH/Admin que respondeu. Nula se essa conta for excluída depois.</summary>
    public int? ResponsavelUsuarioId { get; set; }
    public Usuario? ResponsavelUsuario { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>Token de concorrência: duas pessoas do RH não respondem a mesma mensagem.</summary>
    public int Versao { get; set; }
}
