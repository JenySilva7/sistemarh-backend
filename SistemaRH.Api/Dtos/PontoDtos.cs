using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Uma marcação de ponto, do jeito que a API devolve pro cliente.</summary>
public class RegistroPontoDto
{
    public int Id { get; set; }
    public TipoRegistroPonto Tipo { get; set; }
    public DateTime RegistradoEm { get; set; }
}

/// <summary>Visão do RH: uma marcação de ponto mais quem bateu.</summary>
public class RegistroPontoRhDto : RegistroPontoDto
{
    public int UsuarioId { get; set; }
    public string UsuarioNome { get; set; } = string.Empty;
    public string UsuarioEmail { get; set; } = string.Empty;
}

/// <summary>
/// Resposta de POST /api/ponto: a marcação recém-criada, mais qual é a
/// próxima pendente (ou null se a jornada do dia já foi concluída).
/// </summary>
public class RegistrarPontoResponseDto
{
    public RegistroPontoDto Registro { get; set; } = new();
    public TipoRegistroPonto? Proximo { get; set; }
}
