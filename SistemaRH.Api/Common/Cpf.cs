namespace SistemaRH.Api.Common;

/// <summary>
/// Regras de CPF usadas no cadastro de funcionários: o CPF pode chegar com ou
/// sem máscara ("529.982.247-25" ou "52998224725"), então tudo é comparado
/// pelos 11 dígitos e gravado sempre no formato 000.000.000-00.
/// </summary>
public static class Cpf
{
    /// <summary>Só os dígitos do CPF informado (sem pontos, traço ou espaços).</summary>
    public static string SomenteDigitos(string? cpf) =>
        new((cpf ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>
    /// Confere tamanho e os dois dígitos verificadores. Sequências repetidas
    /// (111.111.111-11 etc.) passam no cálculo, mas não são CPFs válidos.
    /// </summary>
    public static bool EhValido(string? cpf)
    {
        var digitos = SomenteDigitos(cpf);
        if (digitos.Length != 11 || digitos.Distinct().Count() == 1)
        {
            return false;
        }

        var numeros = digitos.Select(c => c - '0').ToArray();
        return numeros[9] == CalcularDigito(numeros, 9) && numeros[10] == CalcularDigito(numeros, 10);
    }

    /// <summary>Formata 11 dígitos como 000.000.000-00.</summary>
    public static string Formatar(string cpf)
    {
        var d = SomenteDigitos(cpf);
        return d.Length == 11 ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}" : cpf.Trim();
    }

    private static int CalcularDigito(int[] numeros, int quantidade)
    {
        var soma = 0;
        for (var i = 0; i < quantidade; i++)
        {
            soma += numeros[i] * (quantidade + 1 - i);
        }

        var resto = soma * 10 % 11;
        return resto == 10 ? 0 : resto;
    }
}
