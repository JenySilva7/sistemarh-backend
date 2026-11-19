using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Corpo de POST /api/mensagens-rh (assunto de 3 a 100 caracteres, mensagem de 3 a 2000).</summary>
public class MensagemRhCreateDto
{
    public string? Assunto { get; set; }
    public string? Mensagem { get; set; }
}

/// <summary>Corpo de PUT /api/rh/mensagens/{id}/resposta (resposta de 3 a 2000 caracteres).</summary>
public class ResponderMensagemRhDto
{
    public string? Resposta { get; set; }

    /// <summary>Versão que estava na tela; se outra pessoa respondeu antes, 409.</summary>
    public int? Versao { get; set; }
}

/// <summary>Uma mensagem ao RH, do jeito que o app e o painel veem.</summary>
public class MensagemRhDto
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string UsuarioNome { get; set; } = string.Empty;
    public string Assunto { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;
    public StatusMensagemRh Status { get; set; }
    public string? Resposta { get; set; }

    /// <summary>Instante da resposta (UTC, com "Z").</summary>
    public DateTime? RespondidaEm { get; set; }
    public string? ResponsavelNome { get; set; }

    /// <summary>Instante do envio (UTC, com "Z").</summary>
    public DateTime CriadoEm { get; set; }
    public int Versao { get; set; }
}
