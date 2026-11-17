using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SistemaRH.Api.Common;

/// <summary>
/// Monta a mensagem única ({ "mensagem": "..." }) dos erros de validação de
/// um DTO. Quando o JSON nem consegue ser convertido (ex.: uma data
/// "31/02/2026"), o ASP.NET gera textos técnicos em inglês ("The JSON value
/// could not be converted...") — esses viram um aviso em português dizendo
/// qual campo está com o formato errado.
/// </summary>
public static partial class MensagensValidacao
{
    private static readonly Dictionary<string, string> NomesDosCampos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dataVigencia"] = "Data de vigência",
        ["dataAdmissao"] = "Data de admissão",
        ["dataNascimento"] = "Data de nascimento",
        ["dataDesligamento"] = "Data de desligamento",
        ["novoSalario"] = "Salário",
        ["novoValor"] = "Valor",
        ["salario"] = "Salário",
        ["valeAlimentacao"] = "Vale alimentação",
        ["novoCargoId"] = "Cargo",
        ["novoDepartamentoId"] = "Departamento",
        ["cargoId"] = "Cargo",
        ["departamentoId"] = "Departamento",
        ["gerenteId"] = "Gestor",
        ["status"] = "Situação",
    };

    public static string Montar(ModelStateDictionary modelState)
    {
        var errosDeFormato = new List<string>();
        var errosDeValidacao = new List<string>();

        foreach (var (chave, entrada) in modelState)
        {
            foreach (var erro in entrada.Errors)
            {
                if (EhErroDeConversao(chave, erro))
                {
                    errosDeFormato.Add($"{NomeAmigavel(chave)} em formato inválido.");
                }
                else if (!string.IsNullOrWhiteSpace(erro.ErrorMessage))
                {
                    errosDeValidacao.Add(erro.ErrorMessage);
                }
            }
        }

        // Quando o corpo não pôde ser lido, o ASP.NET também acrescenta um
        // "The dto field is required." — só o aviso de formato interessa.
        var mensagens = errosDeFormato.Count > 0 ? errosDeFormato : errosDeValidacao;
        return mensagens.Count > 0
            ? string.Join(' ', mensagens.Distinct())
            : "Os dados enviados são inválidos.";
    }

    private static bool EhErroDeConversao(string chave, ModelError erro) =>
        erro.Exception is not null
        || chave.StartsWith('$')
        || erro.ErrorMessage.Contains("could not be converted", StringComparison.OrdinalIgnoreCase)
        || erro.ErrorMessage.Contains("is invalid", StringComparison.OrdinalIgnoreCase);

    private static string NomeAmigavel(string chave)
    {
        // "$.dataVigencia" / "dto.dataVigencia" / "$.itens[0].valor" → último campo.
        var campo = chave.Split('.').LastOrDefault() ?? chave;
        campo = IndiceDeLista().Replace(campo, string.Empty).TrimStart('$');

        if (string.IsNullOrWhiteSpace(campo))
        {
            return "Algum campo";
        }

        return NomesDosCampos.TryGetValue(campo, out var nome) ? nome : $"O campo \"{campo}\"";
    }

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex IndiceDeLista();
}
