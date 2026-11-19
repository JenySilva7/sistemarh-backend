using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SistemaRH.Api.Common;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra criar (ou editar) uma justificativa de ausência.</summary>
public class AusenciaCreateDto
{
    // Anulável de propósito: sem isso, um tipo esquecido no JSON virava
    // "Atestado médico" e uma data esquecida virava 01/01/0001.
    [Required(ErrorMessage = "Selecione o tipo de justificativa.")]
    public TipoAusencia? Tipo { get; set; }

    [Required(ErrorMessage = "Informe a data da ausência.")]
    [JsonConverter(typeof(DataCalendarioJsonConverter))]
    public DateTime? Data { get; set; }

    public bool DiaInteiro { get; set; } = true;

    public string? Descricao { get; set; }

    /// <summary>Só na edição (PUT): a versão que estava na tela. Ignorada ao criar.</summary>
    public int? Versao { get; set; }
}

/// <summary>O que a API devolve ao listar ou consultar uma ausência.</summary>
public class AusenciaDto
{
    public int Id { get; set; }
    public TipoAusencia Tipo { get; set; }
    public DateTime Data { get; set; }
    public bool DiaInteiro { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public StatusAusencia Status { get; set; }
    public DateTime CriadoEm { get; set; }

    /// <summary>Token de concorrência: devolvido no PUT/DELETE e na decisão do RH.</summary>
    public int Versao { get; set; }
}

/// <summary>Visão do RH: a justificativa mais quem enviou.</summary>
public class AusenciaRhDto : AusenciaDto
{
    public int UsuarioId { get; set; }
    public string UsuarioNome { get; set; } = string.Empty;
    public string UsuarioEmail { get; set; } = string.Empty;

    /// <summary>Cadastro de funcionário vinculado à conta de quem enviou (null se a conta não tem vínculo).</summary>
    public int? FuncionarioId { get; set; }
}

/// <summary>Novo status de uma justificativa, definido pelo RH (EmAnalise, Aprovada ou Recusada).</summary>
public class AtualizarStatusAusenciaDto
{
    [Required(ErrorMessage = "Status inválido.")]
    public StatusAusencia? Status { get; set; }

    /// <summary>Versão que estava na tela; se outra pessoa decidiu antes, a resposta é 409.</summary>
    public int? Versao { get; set; }
}
