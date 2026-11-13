namespace SistemaRH.Api.Common;

/// <summary>
/// Conta de "que dia é hoje" no horário de Brasília. Os horários de ponto
/// ficam salvos em UTC, mas o dia de trabalho do colaborador é o dia de
/// Brasília — sem essa conversão, marcações feitas depois das 21h (já é
/// meia-noite em UTC) cairiam no dia seguinte.
/// </summary>
public static class HorarioBrasilia
{
    // Brasil não observa mais horário de verão desde 2019 — Brasília é
    // sempre UTC-3 —, então um offset fixo já é suficiente aqui e evita
    // depender do fuso horário configurado no servidor (que normalmente é
    // UTC dentro do container).
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-3);

    /// <summary>O dia atual (ou do instante UTC informado) em Brasília.</summary>
    public static DateOnly Hoje(DateTime? agoraUtc = null) =>
        DateOnly.FromDateTime((agoraUtc ?? DateTime.UtcNow) + Offset);

    /// <summary>
    /// Intervalo em UTC [início, fim) que corresponde ao dia informado em
    /// Brasília — ex.: 13/11 vai de 13/11 03:00 UTC até 14/11 03:00 UTC.
    /// </summary>
    public static (DateTime InicioUtc, DateTime FimUtc) LimitesDoDiaEmUtc(DateOnly dia)
    {
        var inicio = DateTime.SpecifyKind(dia.ToDateTime(TimeOnly.MinValue) - Offset, DateTimeKind.Utc);
        return (inicio, inicio.AddDays(1));
    }
}
