using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using SistemaRH.Api.Exceptions;

namespace SistemaRH.Api.Common;

/// <summary>
/// Entidades com controle de concorrência otimista: <see cref="Versao"/> sobe
/// a cada alteração (ver AppDbContext.SaveChangesAsync) e o UPDATE só grava
/// se a versão no banco ainda for a que foi lida. Assim, uma tela
/// desatualizada não desfaz o que outra pessoa acabou de gravar.
/// </summary>
public interface IVersionado
{
    int Versao { get; set; }
}

public static class Concorrencia
{
    public const string MensagemConflito =
        "Este registro foi alterado por outra pessoa. Atualize a tela e tente de novo.";

    /// <summary>
    /// Se o cliente mandou a versão que tinha na tela e ela já não é a atual,
    /// alguém gravou antes: 409 em vez de sobrescrever.
    /// </summary>
    public static void ConferirVersao(IVersionado entidade, int? versaoEnviada)
    {
        if (versaoEnviada is not null && versaoEnviada.Value != entidade.Versao)
        {
            throw new ConflictException(MensagemConflito);
        }
    }
}

/// <summary>
/// Traduz os erros do MySQL que acontecem quando duas requisições disputam o
/// mesmo dado (deadlock, chave duplicada) numa resposta amigável — antes
/// eles viravam 500 "erro inesperado".
/// </summary>
public static class ErrosBanco
{
    public const string MensagemDuplicado = "Já existe um registro com esses dados. Atualize a tela e confira.";

    /// <summary>
    /// Deadlock ou espera de trava estourada: não é "alguém alterou o seu
    /// registro" (pode ser até um cadastro novo), só duas operações que se
    /// cruzaram. Tentar de novo costuma funcionar.
    /// </summary>
    public const string MensagemOperacaoSimultanea = "Outra operação estava em andamento ao mesmo tempo. Tente de novo.";

    public sealed record Classificacao(int StatusCode, string Mensagem);

    public static Classificacao? Classificar(Exception excecao)
    {
        if (excecao is DbUpdateConcurrencyException)
        {
            return new(StatusCodes.Status409Conflict, Concorrencia.MensagemConflito);
        }

        return EncontrarMySql(excecao) is { } mysql ? ClassificarNumero(mysql.Number) : null;
    }

    public static Classificacao? ClassificarNumero(int numero) => numero switch
    {
        1062 => new(StatusCodes.Status409Conflict, MensagemDuplicado),                 // ER_DUP_ENTRY
        1213 or 1205 => new(StatusCodes.Status409Conflict, MensagemOperacaoSimultanea), // deadlock / lock wait timeout
        1406 => new(StatusCodes.Status400BadRequest, "Algum campo passou do tamanho máximo permitido."),
        1264 => new(StatusCodes.Status400BadRequest, "Algum valor está fora do limite permitido."),
        _ => null,
    };

    /// <summary>
    /// Nome do índice único violado (ex.: "IX_Funcionarios_Cpf"), pra quem
    /// grava poder trocar a mensagem genérica por uma específica.
    /// </summary>
    public static string? IndiceDuplicado(Exception excecao)
    {
        var mysql = EncontrarMySql(excecao);
        if (mysql is null || mysql.Number != 1062)
        {
            return null;
        }

        // "Duplicate entry 'x' for key 'Funcionarios.IX_Funcionarios_Cpf'"
        var mensagem = mysql.Message;
        var inicio = mensagem.LastIndexOf("for key '", StringComparison.Ordinal);
        if (inicio < 0)
        {
            return string.Empty;
        }
        var chave = mensagem[(inicio + 9)..].TrimEnd('\'');
        return chave.Contains('.') ? chave[(chave.LastIndexOf('.') + 1)..] : chave;
    }

    private static MySqlException? EncontrarMySql(Exception? excecao)
    {
        while (excecao is not null)
        {
            if (excecao is MySqlException mysql)
            {
                return mysql;
            }
            excecao = excecao.InnerException;
        }
        return null;
    }
}
