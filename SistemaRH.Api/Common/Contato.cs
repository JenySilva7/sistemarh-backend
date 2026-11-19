using System.Text.RegularExpressions;
using SistemaRH.Api.Exceptions;

namespace SistemaRH.Api.Common;

/// <summary>
/// Regras de e-mail. O e-mail é o login do app, então ele precisa ser
/// comparado sempre do mesmo jeito: só ASCII (a coluna do banco usa uma
/// collation que ignora acento, e "joão@x.com" virava a mesma conta que
/// "joao@x.com"), sem espaços e gravado em minúsculas.
/// </summary>
public static partial class Emails
{
    public const int TamanhoMaximo = 254;

    private const string MensagemInvalido = "Informe um e-mail válido, no formato usuario@dominio.com.";

    /// <summary>E-mail obrigatório, já normalizado.</summary>
    public static string Normalizar(string? valor, string mensagemObrigatorio = "O e-mail é obrigatório.")
    {
        return NormalizarOpcional(valor) ?? throw new BusinessRuleException(mensagemObrigatorio);
    }

    /// <summary>E-mail opcional: vazio vira null; preenchido precisa ser válido.</summary>
    public static string? NormalizarOpcional(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var email = valor.Trim();
        if (email.Length > TamanhoMaximo)
        {
            throw new BusinessRuleException($"O e-mail pode ter no máximo {TamanhoMaximo} caracteres.");
        }
        if (!TryNormalizar(email, out var normalizado))
        {
            throw new BusinessRuleException(MensagemInvalido);
        }
        return normalizado;
    }

    /// <summary>
    /// Versão que não lança exceção, usada no login: lá um e-mail inválido
    /// recebe a mesma mensagem genérica de "e-mail ou senha incorretos".
    /// </summary>
    public static bool TryNormalizar(string? valor, out string email)
    {
        email = string.Empty;
        if (string.IsNullOrWhiteSpace(valor))
        {
            return false;
        }

        var candidato = valor.Trim();
        if (candidato.Length > TamanhoMaximo || candidato.Any(c => c < '!' || c > '~'))
        {
            // Fora do ASCII visível: acento, espaço no meio, caractere de controle...
            return false;
        }

        candidato = candidato.ToLowerInvariant();
        var arroba = candidato.IndexOf('@');
        if (arroba is < 1 or > 64 || !Formato().IsMatch(candidato))
        {
            return false;
        }

        email = candidato;
        return true;
    }

    // Parte local (até 64): letras, dígitos e os caracteres comuns do RFC
    // 5322 — . ! # $ % & ' * + / = ? ^ _ ` { | } ~ - —, sem ponto no começo,
    // no fim nem "..". Assim "o'neil@empresa.com" é aceito, como no painel.
    // Domínio: rótulos de 1 a 63 (letras, dígitos e hífen fora das pontas)
    // e um TLD só de letras, com 2 a 63.
    [GeneratedRegex(@"^[a-z0-9!#$%&'*+/=?^_`{|}~\-]+(\.[a-z0-9!#$%&'*+/=?^_`{|}~\-]+)*@([a-z0-9]([a-z0-9\-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex Formato();
}

/// <summary>
/// Telefone brasileiro: DDD + número, com 10 (fixo) ou 11 dígitos (celular,
/// que sempre começa com 9). É gravado já formatado, pra todo cadastro ficar
/// igual na tela.
/// </summary>
public static class Telefones
{
    private const string MensagemInvalido =
        "Telefone inválido. Informe DDD + número, com 10 ou 11 dígitos (ex.: (11) 98765-4321).";

    public static string? NormalizarOpcional(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        // Só dígitos ASCII e a pontuação de máscara. Letras, "+", "<script>"
        // ou dígitos de outros alfabetos fazem o telefone ser recusado.
        var texto = valor.Trim();
        if (texto.Length > 30 || texto.Any(c => !(c is >= '0' and <= '9' or ' ' or '(' or ')' or '-' or '.')))
        {
            throw new BusinessRuleException(MensagemInvalido);
        }

        var d = new string(texto.Where(c => c is >= '0' and <= '9').ToArray());
        var ddd = d.Length >= 2 ? int.Parse(d[..2]) : 0;
        var valido = d.Length is 10 or 11
            && ddd is >= 11 and <= 99
            && (d.Length == 10 || d[2] == '9');
        if (!valido)
        {
            throw new BusinessRuleException(MensagemInvalido);
        }

        return d.Length == 11
            ? $"({d[..2]}) {d[2..7]}-{d[7..]}"
            : $"({d[..2]}) {d[2..6]}-{d[6..]}";
    }
}

/// <summary>
/// Senha que o RH define pro colaborador entrar no app (e a das contas
/// criadas pelo Admin). O tamanho é contado em caracteres de verdade (code
/// points), e não em unidades UTF-16. Caracteres de controle ou invisíveis
/// são recusados em qualquer posição (a mesma lista dos textos, ver
/// <see cref="Textos.TemInvisivelOuControle"/>): com eles dava pra definir uma
/// senha que parece vazia (6 × U+200B) ou que ninguém consegue digitar. Emoji
/// também (ver <see cref="Textos.TemEmoji"/>).
/// </summary>
public static class Senhas
{
    public const int Minimo = 6;
    public const int Maximo = 64;
    public const string MensagemEmojiOuInvisivel = "A senha não pode conter emoji nem caracteres invisíveis.";

    public static void Validar(string? senha, string rotulo = "A senha de acesso ao app")
    {
        var mensagemCurta = $"{rotulo} precisa ter pelo menos {Minimo} caracteres.";
        if (string.IsNullOrEmpty(senha))
        {
            throw new BusinessRuleException(mensagemCurta);
        }
        if (string.IsNullOrWhiteSpace(senha))
        {
            throw new BusinessRuleException($"{rotulo} não pode ser formada só por espaços.");
        }
        if (Textos.TemInvisivelOuControle(senha) || Textos.TemEmoji(senha))
        {
            // Emoji fica de fora da senha: o mesmo coração chega como "❤" de
            // um teclado e "❤️" (com o seletor invisível U+FE0F) de outro, e a
            // pessoa não entra mais pelo outro aparelho. A mensagem diz o que
            // fazer — antes, quem digitou um coração lia "caracteres de controle".
            throw new BusinessRuleException(MensagemEmojiOuInvisivel);
        }
        if (char.IsWhiteSpace(senha[0]) || char.IsWhiteSpace(senha[^1]))
        {
            // Espaço na ponta some fácil ao copiar e colar e a pessoa não
            // consegue mais entrar (a tela dizia "já pode entrar no app").
            throw new BusinessRuleException($"{rotulo} não pode começar nem terminar com espaço.");
        }

        var caracteres = senha.EnumerateRunes().Count();
        if (caracteres < Minimo)
        {
            throw new BusinessRuleException(mensagemCurta);
        }
        if (caracteres > Maximo)
        {
            throw new BusinessRuleException($"{rotulo} pode ter no máximo {Maximo} caracteres.");
        }
    }
}
