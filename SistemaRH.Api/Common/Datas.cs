using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SistemaRH.Api.Exceptions;

namespace SistemaRH.Api.Common;

/// <summary>
/// Faixas de datas aceitas pela API. Sem elas, uma data esquecida no JSON
/// virava 01/01/0001 em silêncio, e passavam nascimentos no ano 9999,
/// admissões antes do nascimento e vigências no ano 1.
/// Todas as contas usam o "hoje" de Brasília (ver HorarioBrasilia).
/// </summary>
public static class Datas
{
    public static readonly DateTime MinimoAbsoluto = new(1900, 1, 1);

    /// <summary>Mensagem interna dos conversores de data (vira "X está em formato inválido." na resposta).</summary>
    public const string MensagemFormatoData = "Data em formato inválido.";

    /// <summary>
    /// Data e hora no horário de Brasília, sem fuso: um valor em UTC (ou no
    /// fuso local do servidor) é convertido; um sem fuso já é de Brasília.
    /// </summary>
    public static DateTime ParaHorarioDeBrasilia(DateTime valor) => valor.Kind switch
    {
        DateTimeKind.Utc => DateTime.SpecifyKind(valor + HorarioBrasilia.Offset, DateTimeKind.Unspecified),
        DateTimeKind.Local => DateTime.SpecifyKind(valor.ToUniversalTime() + HorarioBrasilia.Offset, DateTimeKind.Unspecified),
        _ => valor,
    };

    /// <summary>Hoje em Brasília, como data de calendário (meia-noite, sem fuso).</summary>
    public static DateTime Hoje() => HorarioBrasilia.Hoje().ToDateTime(TimeOnly.MinValue);

    /// <summary>Só o dia, sem horário nem fuso (como as outras datas de calendário do banco).</summary>
    public static DateTime SoDia(DateTime data) => DateTime.SpecifyKind(data.Date, DateTimeKind.Unspecified);

    public static string Formatar(DateTime data) => data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Nascimento (opcional): de 01/01/1900 até hoje menos 14 anos.</summary>
    public static DateTime? ValidarNascimento(DateTime? nascimento)
    {
        if (nascimento is null)
        {
            return null;
        }

        var dia = SoDia(nascimento.Value);
        var maximo = Hoje().AddYears(-14);
        if (dia < MinimoAbsoluto || dia > maximo)
        {
            throw new BusinessRuleException(
                $"A data de nascimento precisa estar entre {Formatar(MinimoAbsoluto)} e {Formatar(maximo)} (o colaborador precisa ter pelo menos 14 anos).");
        }
        return dia;
    }

    /// <summary>Admissão (obrigatória): de nascimento + 14 anos até hoje + 90 dias.</summary>
    public static DateTime ValidarAdmissao(DateTime? admissao, DateTime? nascimento)
    {
        if (admissao is null)
        {
            throw new BusinessRuleException("A data de admissão é obrigatória.");
        }

        var dia = SoDia(admissao.Value);
        var minimo = nascimento is DateTime n ? SoDia(n).AddYears(14) : MinimoAbsoluto;
        var maximo = Hoje().AddDays(90);
        if (dia < minimo || dia > maximo)
        {
            var explicacao = nascimento is null ? string.Empty : " (pelo menos 14 anos depois do nascimento)";
            throw new BusinessRuleException(
                $"A data de admissão precisa estar entre {Formatar(minimo)} e {Formatar(maximo)}{explicacao}.");
        }
        return dia;
    }

    /// <summary>
    /// Data de vigência de um ajuste: da admissão até hoje. O novo valor é
    /// aplicado na hora, então uma vigência futura gravaria um valor que
    /// ainda não deveria valer. Exceção: quem ainda vai ser admitido
    /// (admissão futura, permitida até hoje + 90 dias) teria a janela vazia e
    /// nenhum ajuste possível — pra essa pessoa, a vigência é a própria data
    /// de admissão. A janela é [admissão, max(hoje, admissão)].
    /// </summary>
    public static DateTime ValidarVigencia(DateTime? vigencia, DateTime admissao)
    {
        if (vigencia is null)
        {
            throw new BusinessRuleException("Informe a data de vigência.");
        }

        var dia = SoDia(vigencia.Value);
        var inicio = SoDia(admissao);
        var hoje = Hoje();
        if (inicio > hoje)
        {
            if (dia != inicio)
            {
                throw new BusinessRuleException(
                    $"Para quem ainda vai ser admitido, a vigência é a própria data de admissão ({Formatar(inicio)}).");
            }
            return dia;
        }

        if (dia > hoje)
        {
            throw new BusinessRuleException("Ajustes com data futura ainda não são suportados.");
        }
        if (dia < inicio)
        {
            throw new BusinessRuleException(
                $"A data de vigência não pode ser anterior à admissão ({Formatar(inicio)}).");
        }
        return dia;
    }

    /// <summary>
    /// Desligamento: da admissão até hoje — ou até a própria admissão, se ela
    /// ainda vai acontecer. Sem data, vale hoje (ou a admissão futura). Antes a
    /// faixa ia só até hoje e ficava vazia pra quem tinha admissão futura:
    /// quem desistia antes de começar não podia ser desligado e seguia ativo
    /// nas listas, no Organograma e como gestor possível até o dia da admissão.
    /// </summary>
    public static DateTime ValidarDesligamento(DateTime? desligamento, DateTime admissao)
    {
        var inicio = SoDia(admissao);
        var hoje = Hoje();
        var ultimoDia = inicio > hoje ? inicio : hoje;
        var dia = desligamento is DateTime d ? SoDia(d) : ultimoDia;
        if (dia > ultimoDia)
        {
            throw new BusinessRuleException(inicio > hoje
                ? $"Para quem ainda vai ser admitido, o desligamento é na própria data de admissão ({Formatar(inicio)})."
                : "A data de desligamento não pode ser futura.");
        }
        if (dia < inicio)
        {
            throw new BusinessRuleException(
                $"A data de desligamento não pode ser anterior à admissão ({Formatar(admissao)}).");
        }
        return dia;
    }

    /// <summary>Validação genérica de uma data obrigatória dentro de [mínimo, máximo].</summary>
    public static DateTime ValidarFaixa(DateTime? data, string mensagemObrigatorio, string rotulo, DateTime minimo, DateTime maximo)
    {
        if (data is null)
        {
            throw new BusinessRuleException(mensagemObrigatorio);
        }

        var dia = SoDia(data.Value);
        if (dia < SoDia(minimo) || dia > SoDia(maximo))
        {
            throw new BusinessRuleException(
                $"{rotulo} precisa estar entre {Formatar(minimo)} e {Formatar(maximo)}.");
        }
        return dia;
    }
}

/// <summary>
/// Lê uma data de calendário do JSON pegando o dia COMO FOI ESCRITO. O
/// conversor padrão aplica o fuso: "2026-11-17T00:00:00+14:00" virava 16/11
/// no horário do servidor. Aqui "2026-11-17", "2026-11-17T23:30:00-10:00" e
/// "2026-11-17T01:30:00Z" são todos o dia 17/11. Qualquer outro texto vira
/// erro de formato (400 com "... está em formato inválido.").
/// </summary>
public sealed class DataCalendarioJsonConverter : JsonConverter<DateTime?>
{
    public override bool HandleNull => true;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(Datas.MensagemFormatoData);
        }

        var texto = reader.GetString() ?? string.Empty;
        if (texto.Length < 10
            || !DateTime.TryParseExact(texto[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
        {
            throw new JsonException(Datas.MensagemFormatoData);
        }

        // Se veio com horário/fuso, o resto também precisa ser válido — só
        // não é usado.
        if (texto.Length > 10
            && (texto[10] != 'T' || !DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)))
        {
            throw new JsonException(Datas.MensagemFormatoData);
        }

        return DateTime.SpecifyKind(dia, DateTimeKind.Unspecified);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Data e hora de um compromisso (agenda), sempre no horário de Brasília e
/// sem fuso — como as outras datas da API. Com o conversor padrão,
/// "2026-12-01T00:30:00+14:00" era convertido pro fuso do servidor e
/// "2026-12-01T02:00:00Z" ficava em UTC: dois jeitos de gravar, e o GET
/// devolvia sem fuso, então o horário lido saía errado.
/// Agora: com fuso (ou "Z"), o instante é convertido pra Brasília (UTC-3);
/// sem fuso, é lido como já sendo de Brasília. A resposta sai sem fuso.
/// </summary>
public sealed class DataHoraBrasiliaJsonConverter : JsonConverter<DateTime?>
{
    public override bool HandleNull => true;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(Datas.MensagemFormatoData);
        }

        var texto = (reader.GetString() ?? string.Empty).Trim();
        if (TemFuso(texto))
        {
            if (!DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instante))
            {
                throw new JsonException(Datas.MensagemFormatoData);
            }
            return DateTime.SpecifyKind(instante.UtcDateTime + HorarioBrasilia.Offset, DateTimeKind.Unspecified);
        }

        string[] formatos = ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF"];
        if (!DateTime.TryParseExact(texto, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            throw new JsonException(Datas.MensagemFormatoData);
        }
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
    }

    /// <summary>"Z" no fim ou "+hh:mm"/"-hh:mm" depois do horário.</summary>
    private static bool TemFuso(string texto)
    {
        if (texto.EndsWith('Z') || texto.EndsWith('z'))
        {
            return true;
        }
        var t = texto.IndexOf('T');
        return t > 0 && texto.IndexOfAny(['+', '-'], t) > 0;
    }
}
