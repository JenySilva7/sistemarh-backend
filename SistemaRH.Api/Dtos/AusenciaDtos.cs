using System.ComponentModel.DataAnnotations;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Dados pra criar (ou editar) uma justificativa de ausência.</summary>
public class AusenciaCreateDto
{
    [Required(ErrorMessage = "Selecione o tipo de justificativa.")]
    public TipoAusencia Tipo { get; set; }

    [Required(ErrorMessage = "Informe a data da ausência.")]
    public DateTime Data { get; set; }

    public bool DiaInteiro { get; set; } = true;

    [Required(ErrorMessage = "Descreva o motivo da ausência.")]
    [MinLength(3, ErrorMessage = "Descreva o motivo com um pouco mais de detalhe.")]
    public string Descricao { get; set; } = string.Empty;
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
    public StatusAusencia Status { get; set; }
}
