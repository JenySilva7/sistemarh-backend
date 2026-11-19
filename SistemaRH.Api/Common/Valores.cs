using SistemaRH.Api.Exceptions;

namespace SistemaRH.Api.Common;

/// <summary>
/// Valores em dinheiro. As colunas do banco guardam 2 casas decimais: antes,
/// "2345.678" era gravado como 2345,68 enquanto a resposta devolvia 2345,678,
/// e valores acima da coluna davam 500. Agora o que não cabe é recusado.
/// </summary>
public static class Dinheiro
{
    public const decimal SalarioMinimo = 1.00m;
    public const decimal SalarioMaximo = 1_000_000.00m;
    public const decimal ValeMaximo = 10_000.00m;
    public const decimal FolhaMaximo = 1_000_000.00m;

    public const string MensagemFaixaSalario = "O salário precisa estar entre R$ 1,00 e R$ 1.000.000,00.";
    public const string MensagemFaixaVale = "O vale alimentação precisa estar entre R$ 0,00 e R$ 10.000,00.";
    public const string MensagemIgualAoAtual = "O novo valor é igual ao atual.";

    /// <param name="campo">Nome do campo com artigo, pra mensagem de casas decimais ("o salário").</param>
    public static decimal Validar(decimal? valor, string mensagemObrigatorio, decimal minimo, decimal maximo, string mensagemFaixa, string campo)
    {
        if (valor is null)
        {
            throw new BusinessRuleException(mensagemObrigatorio);
        }

        var v = valor.Value;
        if (v < minimo || v > maximo)
        {
            throw new BusinessRuleException(mensagemFaixa);
        }
        if (decimal.Round(v, 2) != v)
        {
            throw new BusinessRuleException($"Informe {campo} com no máximo 2 casas decimais.");
        }
        // Normaliza a escala (5000.000 → 5000.00) pra resposta sair igual ao gravado.
        return decimal.Round(v, 2);
    }

    public static decimal ValidarSalario(decimal? valor, string mensagemObrigatorio = "Informe o novo salário.") =>
        Validar(valor, mensagemObrigatorio, SalarioMinimo, SalarioMaximo, MensagemFaixaSalario, "o salário");

    public static decimal ValidarVale(decimal? valor, string mensagemObrigatorio = "Informe o novo valor do vale alimentação.") =>
        Validar(valor, mensagemObrigatorio, 0m, ValeMaximo, MensagemFaixaVale, "o vale alimentação");
}

/// <summary>Conferência de enums que chegam do cliente (o JSON aceita qualquer número, como 99 ou -1).</summary>
public static class Enums
{
    public static T Validar<T>(T? valor, string mensagemObrigatorio, string mensagemInvalido) where T : struct, Enum
    {
        if (valor is null)
        {
            throw new BusinessRuleException(mensagemObrigatorio);
        }
        if (!Enum.IsDefined(valor.Value))
        {
            throw new BusinessRuleException(mensagemInvalido);
        }
        return valor.Value;
    }

    /// <summary>Filtro opcional (ex.: ?status=): só confere se veio.</summary>
    public static void ValidarFiltro<T>(T? valor, string mensagemInvalido) where T : struct, Enum
    {
        if (valor is not null && !Enum.IsDefined(valor.Value))
        {
            throw new BusinessRuleException(mensagemInvalido);
        }
    }
}
