using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SistemaRH.Api.Common;

/// <summary>
/// Monta a mensagem única ({ "mensagem": "..." }) dos erros de validação de
/// um DTO. Quando o JSON nem consegue ser convertido (ex.: uma data
/// "31/02/2026") ou a query traz um valor que não é do tipo esperado
/// (?status=abc), o ASP.NET gera textos técnicos em inglês ("The value 'abc'
/// is not valid.", "A non-empty request body is required.") — e alguns ainda
/// repetem o que o cliente mandou. Aqui tudo vira um aviso em português que
/// só diz QUAL campo está errado, nunca o valor enviado.
/// </summary>
public static partial class MensagensValidacao
{
    public const string MensagemCorpoVazio = "O corpo da requisição está vazio. Envie os dados em JSON.";
    public const string MensagemJsonInvalido = "O corpo da requisição não é um JSON válido.";

    private static readonly Dictionary<string, string> NomesDosCampos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dataVigencia"] = "Data de vigência",
        ["dataAdmissao"] = "Data de admissão",
        ["dataNascimento"] = "Data de nascimento",
        ["dataDesligamento"] = "Data de desligamento",
        ["dataAvaliacao"] = "Data da avaliação",
        ["dataEvento"] = "Data do evento",
        ["dataRegistro"] = "Data do lançamento",
        ["dataCandidatura"] = "Data da candidatura",
        ["dataPagamento"] = "Data de pagamento",
        ["data"] = "Data",
        ["de"] = "Data inicial",
        ["ate"] = "Data final",
        ["novoSalario"] = "Salário",
        ["novoValor"] = "Valor",
        ["salario"] = "Salário",
        ["salarioBase"] = "Salário base",
        ["valeAlimentacao"] = "Vale alimentação",
        ["bonus"] = "Bônus",
        ["descontos"] = "Descontos",
        ["nota"] = "Nota",
        ["quantidadeMinutos"] = "Quantidade de minutos",
        ["novoCargoId"] = "Cargo",
        ["novoDepartamentoId"] = "Departamento",
        ["novoGerenteId"] = "Gestor",
        ["cargoId"] = "Cargo",
        ["departamentoId"] = "Departamento",
        ["funcionarioId"] = "Funcionário",
        ["gerenteId"] = "Gestor",
        ["status"] = "Situação",
        ["tipo"] = "Tipo",
        ["perfil"] = "Perfil",
        ["perfilAtual"] = "Perfil atual",
        ["senhaProvisoria"] = "Senha provisória",
        ["senhaAtual"] = "Senha atual",
        ["novaSenha"] = "Nova senha",
        ["versao"] = "Versão",
        ["diaInteiro"] = "Dia inteiro",
        ["nome"] = "Nome",
        ["nomeCandidato"] = "Nome do candidato",
        ["email"] = "E-mail",
        ["senha"] = "Senha",
        ["senhaAcessoApp"] = "Senha de acesso ao app",
        ["cpf"] = "CPF",
        ["telefone"] = "Telefone",
        ["descricao"] = "Descrição",
        ["motivo"] = "Motivo",
        ["assunto"] = "Assunto",
        ["mensagem"] = "Mensagem",
        ["resposta"] = "Resposta",
        ["titulo"] = "Título",
        ["comentario"] = "Comentário",
        ["observacoes"] = "Observações",
        ["vaga"] = "Vaga",
        ["competencia"] = "Competência",
        ["id"] = "Id",
    };

    public static string Montar(ModelStateDictionary modelState)
    {
        var errosDeFormato = new List<string>();
        var errosDeValidacao = new List<string>();
        var corpoVazio = false;

        foreach (var (chave, entrada) in modelState)
        {
            foreach (var erro in entrada.Errors)
            {
                var texto = erro.ErrorMessage ?? string.Empty;

                if (texto.Contains("non-empty request body", StringComparison.OrdinalIgnoreCase))
                {
                    corpoVazio = true;
                }
                else if (EhErroDeSintaxeJson(chave, erro))
                {
                    // JSON truncado ou malformado: o caminho do erro aponta o
                    // campo onde o leitor parou, não um campo com valor errado.
                    errosDeFormato.Add(MensagemJsonInvalido);
                }
                else if (EhErroDeConversao(chave, erro))
                {
                    errosDeFormato.Add(MensagemDeFormato(chave));
                }
                else if (CampoObrigatorioEmIngles().IsMatch(texto))
                {
                    // "The dto field is required." vem junto do corpo vazio;
                    // nos demais casos é um campo que faltou.
                    if (EhParametroInteiro(chave))
                    {
                        corpoVazio = true;
                    }
                    else
                    {
                        errosDeValidacao.Add($"Campo obrigatório: {NomeAmigavel(chave)}.");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(texto))
                {
                    errosDeValidacao.Add(PareceTextoDoFramework(texto) ? "Os dados enviados são inválidos." : texto);
                }
            }
        }

        if (corpoVazio)
        {
            return MensagemCorpoVazio;
        }

        // Quando o corpo não pôde ser lido, o ASP.NET também acrescenta um
        // "campo obrigatório" — só o aviso de formato interessa.
        var mensagens = errosDeFormato.Count > 0 ? errosDeFormato : errosDeValidacao;
        return mensagens.Count > 0
            ? string.Join(' ', mensagens.Distinct())
            : "Os dados enviados são inválidos.";
    }

    private static bool EhErroDeConversao(string chave, ModelError erro) =>
        erro.Exception is not null
        || chave.StartsWith('$')
        || erro.ErrorMessage.Contains("could not be converted", StringComparison.OrdinalIgnoreCase)
        || erro.ErrorMessage.Contains("is not valid", StringComparison.OrdinalIgnoreCase)
        || erro.ErrorMessage.Contains("is invalid", StringComparison.OrdinalIgnoreCase)
        || erro.ErrorMessage.Contains("must be a number", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Erro de sintaxe do JSON (texto cortado, vírgula faltando...), e não de
    /// um valor que não converte pro tipo do campo. O System.Text.Json lança
    /// os de sintaxe a partir do leitor (JsonReaderException) e os de tipo com
    /// "could not be converted"; os conversores de data da API usam
    /// <see cref="Datas.MensagemFormatoData"/>.
    /// </summary>
    private static bool EhErroDeSintaxeJson(string chave, ModelError erro)
    {
        if (!chave.StartsWith('$'))
        {
            return false;
        }
        for (var excecao = erro.Exception; excecao is not null; excecao = excecao.InnerException)
        {
            if (excecao.GetType().Name == "JsonReaderException")
            {
                return true;
            }
        }

        var texto = erro.ErrorMessage ?? string.Empty;
        return texto.Length > 0
            && !texto.Contains("could not be converted", StringComparison.OrdinalIgnoreCase)
            && !texto.StartsWith(Datas.MensagemFormatoData, StringComparison.Ordinal);
    }

    // Campos cujo erro de formato tem mensagem própria, igual à da validação
    // do serviço (ex.: status "abc", 9 ou null dão todos "Status inválido.").
    private static readonly Dictionary<string, string> MensagensDeFormatoProprias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["status"] = MensagemStatusInvalido,
        ["perfil"] = "Perfil inválido.",
        ["perfilAtual"] = "Perfil inválido.",
    };

    public const string MensagemStatusInvalido = "Status inválido.";

    private static string MensagemDeFormato(string chave)
    {
        var campo = UltimoCampo(chave);
        if (string.IsNullOrEmpty(campo))
        {
            // Erro no JSON como um todo ("{quebrado", array no lugar de objeto...).
            return MensagemJsonInvalido;
        }
        if (MensagensDeFormatoProprias.TryGetValue(campo, out var propria))
        {
            return propria;
        }
        return $"{NomeAmigavel(chave)} está em formato inválido.";
    }

    /// <summary>A chave sem ponto (ex.: "dto") é o parâmetro inteiro do corpo, não um campo dele.</summary>
    private static bool EhParametroInteiro(string chave) =>
        !chave.Contains('.') && !NomesDosCampos.ContainsKey(chave);

    private static bool PareceTextoDoFramework(string texto) =>
        texto.StartsWith("The ", StringComparison.Ordinal)
        || texto.Contains("JSON", StringComparison.Ordinal)
        || texto.Contains(" must be ", StringComparison.Ordinal);

    private static string UltimoCampo(string chave)
    {
        // "$.dataVigencia" / "dto.dataVigencia" / "$.itens[0].valor" / "$['x']" → último campo.
        var campo = chave.Split('.').LastOrDefault() ?? chave;
        return IndiceDeLista().Replace(campo, string.Empty).TrimStart('$');
    }

    private static string NomeAmigavel(string chave)
    {
        var campo = UltimoCampo(chave);
        if (NomesDosCampos.TryGetValue(campo, out var nome))
        {
            return nome;
        }

        // Só repete o nome do campo se ele for um identificador comum: um
        // nome de propriedade inventado pelo cliente nunca volta na resposta.
        return IdentificadorSimples().IsMatch(campo) ? $"O campo \"{campo}\"" : "Um dos campos";
    }

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex IndiceDeLista();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]{0,39}$")]
    private static partial Regex IdentificadorSimples();

    [GeneratedRegex(@"^The .+ field is required\.$")]
    private static partial Regex CampoObrigatorioEmIngles();
}
