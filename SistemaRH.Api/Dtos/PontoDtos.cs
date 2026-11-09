using SistemaRH.Api.Models;

namespace SistemaRH.Api.Dtos;

/// <summary>Uma marcação de ponto, do jeito que a API devolve pro cliente.</summary>
public class RegistroPontoDto
{
    public int Id { get; set; }
    public TipoRegistroPonto Tipo { get; set; }
    public DateTime RegistradoEm { get; set; }
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
