namespace SistemaRH.Api.Models;

/// <summary>
/// Os 4 tipos de marcação do dia, na ordem em que devem ser batidos.
/// Mesma sequência usada no app Flutter (PontoController).
/// </summary>
public enum TipoRegistroPonto
{
    Entrada = 0,
    SaidaAlmoco = 1,
    VoltaAlmoco = 2,
    Saida = 3,
}

/// <summary>
/// Uma marcação de ponto batida por um usuário. Não existe edição nem
/// exclusão de ponto — uma vez registrado, o horário é definitivo (reflete
/// como ponto eletrônico funciona na vida real).
/// </summary>
public class RegistroPonto
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public TipoRegistroPonto Tipo { get; set; }
    public DateTime RegistradoEm { get; set; }
}
