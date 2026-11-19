using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SistemaRH.Api.Exceptions;

namespace SistemaRH.Api.Common;

/// <summary>
/// Regra de um campo de texto: como ele se chama na mensagem de erro, o
/// tamanho mínimo e máximo (contados DEPOIS da limpeza) e se aceita quebra
/// de linha. O máximo é o mesmo tamanho da coluna no banco (ver
/// AppDbContext), então um texto validado aqui nunca estoura a coluna.
/// </summary>
public sealed record RegraTexto(
    string Rotulo,
    int Minimo,
    int Maximo,
    string MensagemObrigatorio,
    bool MultiLinha = false,
    bool PermiteEmoji = false)
{
    /// <summary>
    /// Aceita as partes "invisíveis" das sequências de emoji (❤️, 👍🏽,
    /// 👨‍👩‍👧, 🏳️‍🌈). Vale pros textos longos e pro assunto da mensagem ao
    /// RH — quem escreve pelo celular usa emoji. Nome, departamento, cargo,
    /// e-mail e senha seguem a regra estrita.
    /// </summary>
    public bool AceitaEmoji => MultiLinha || PermiteEmoji;
}

/// <summary>
/// Limpeza e validação de todo texto livre que chega na API. Sem isso,
/// nomes com caracteres invisíveis (zero-width) ou de direção (RTL override)
/// passavam e apareciam "disfarçados" na tela, "  a  " contava como 5
/// caracteres e um nome de 90 mil caracteres derrubava a listagem inteira.
/// </summary>
public static partial class Textos
{
    // ── Regras usadas pelos serviços ─────────────────────────────────────
    public static readonly RegraTexto NomePessoa = new("O nome", 3, 120, "O nome é obrigatório.");
    public static readonly RegraTexto NomeCandidato = new("O nome do candidato", 3, 120, "O nome do candidato é obrigatório.");
    public static readonly RegraTexto NomeDepartamento = new("O nome do departamento", 2, 100, "O nome do departamento é obrigatório.");
    public static readonly RegraTexto NomeCargo = new("O nome do cargo", 2, 100, "O nome do cargo é obrigatório.");
    public static readonly RegraTexto DescricaoCurta = new("A descrição", 0, 500, string.Empty);
    public static readonly RegraTexto DescricaoLonga = new("A descrição", 0, 2000, string.Empty, MultiLinha: true);
    public static readonly RegraTexto Motivo = new("O motivo", 3, 500, "Informe o motivo do ajuste.", MultiLinha: true);
    public static readonly RegraTexto DescricaoAusencia = new("A descrição", 3, 2000, "Descreva o motivo da ausência.", MultiLinha: true);
    public static readonly RegraTexto AssuntoMensagem = new("O assunto", 3, 100, "Informe o assunto da mensagem.", PermiteEmoji: true);
    public static readonly RegraTexto CorpoMensagem = new("A mensagem", 3, 2000, "Escreva a mensagem.", MultiLinha: true);
    public static readonly RegraTexto RespostaMensagem = new("A resposta", 3, 2000, "Escreva a resposta.", MultiLinha: true);
    public static readonly RegraTexto TituloEvento = new("O título", 2, 100, "O título é obrigatório.");
    public static readonly RegraTexto Comentario = new("O comentário", 0, 2000, string.Empty, MultiLinha: true);
    public static readonly RegraTexto Observacoes = new("O campo observações", 0, 2000, string.Empty, MultiLinha: true);
    public static readonly RegraTexto Vaga = new("A vaga", 2, 100, "Informe a vaga.");

    /// <summary>
    /// Caracteres que não são de controle/formatação (Cc/Cf) mas também não
    /// aparecem na tela — com eles dava pra ter um nome "em branco" que passa
    /// no tamanho mínimo, ou dois nomes "gêmeos" que parecem iguais. O painel e
    /// o app copiam esta mesma lista (ver README, seção 8).
    /// </summary>
    public static bool EhInvisivelExtra(int codePoint) => codePoint switch
    {
        0x034F => true,                       // combining grapheme joiner
        0x115F or 0x1160 => true,             // fillers do hangul (jamo)
        0x3164 or 0xFFA0 => true,             // hangul filler / halfwidth hangul filler
        0x17B4 or 0x17B5 => true,             // vogais invisíveis do khmer
        0x2800 => true,                       // braille em branco
        >= 0x180B and <= 0x180F => true,      // seletores de variação (FVS) e separador do mongol
        >= 0xFE00 and <= 0xFE0F => true,      // seletores de variação VS1–VS16
        >= 0xE0100 and <= 0xE01EF => true,    // seletores de variação VS17–VS256
        _ => false,
    };

    /// <summary>Limpa e valida um texto obrigatório.</summary>
    public static string Validar(string? valor, RegraTexto regra)
    {
        var limpo = Limpar(valor, regra);
        // Precisa de pelo menos um caractere visível de verdade (as partes de
        // emoji, sozinhas, não contam).
        if (limpo.Length == 0 || !limpo.EnumerateRunes().Any(EhVisivel))
        {
            throw new BusinessRuleException(regra.MensagemObrigatorio);
        }
        ConferirTamanho(limpo, regra);
        return limpo;
    }

    /// <summary>
    /// Partes de uma sequência de emoji que não são um caractere por si:
    /// seletores de variação de texto/emoji (U+FE0E, U+FE0F), o zero-width
    /// joiner (U+200D), os modificadores de tom de pele (U+1F3FB–U+1F3FF), as
    /// tags das bandeiras de região (U+E0020–U+E007F) e o keycap (U+20E3).
    /// Não contam pro tamanho mínimo nem como "caractere visível".
    /// </summary>
    public static bool EhParteDeEmoji(int codePoint) => codePoint switch
    {
        0xFE0E or 0xFE0F or 0x200D or 0x20E3 => true,
        >= 0x1F3FB and <= 0x1F3FF => true,
        >= 0xE0020 and <= 0xE007F => true,
        _ => false,
    };

    /// <summary>
    /// Base de emoji "desenhado": pode receber seletor de variação, tom de
    /// pele e ser unida a outra pelo ZWJ. Faixas de símbolos e pictogramas
    /// (U+1F000–U+1FAFF, U+2600–U+27BF, U+2300–U+23FF, U+2B00–U+2BFF, setas
    /// U+2190–U+21FF e U+2900–U+297F) e os avulsos que têm forma de emoji
    /// (〰 〽 ㊗ ㊙ © ® ‼ ⁉ ™ ℹ Ⓜ ▪ ▫ ▶ ◀ ◻ ◼ ◽ ◾). Os tons de pele, apesar
    /// de estarem na faixa, não são base: "👍🏽🏽" não pode.
    /// </summary>
    public static bool EhBaseDeEmoji(int codePoint) => codePoint switch
    {
        >= 0x1F3FB and <= 0x1F3FF => false,
        >= 0x1F000 and <= 0x1FAFF => true,
        >= 0x2600 and <= 0x27BF => true,
        >= 0x2300 and <= 0x23FF => true,
        >= 0x2B00 and <= 0x2BFF => true,
        >= 0x2190 and <= 0x21FF => true,
        >= 0x2900 and <= 0x297F => true,
        0x3030 or 0x303D or 0x3297 or 0x3299 => true,
        0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 => true,
        0x24C2 or 0x25AA or 0x25AB or 0x25B6 or 0x25C0 or (>= 0x25FB and <= 0x25FE) => true,
        _ => false,
    };

    /// <summary>Base de keycap (1️⃣, #️⃣, *️⃣): aceita o seletor e o U+20E3, mas não tom de pele nem ZWJ.</summary>
    private static bool EhBaseDeKeycap(int codePoint) => codePoint is (>= '0' and <= '9') or '#' or '*';

    private static bool EhTag(int codePoint) => codePoint is >= 0xE0020 and <= 0xE007E;

    private const int FimDasTags = 0xE007F;
    private const int BandeiraPreta = 0x1F3F4;
    private const int MaximoDeZwjPorSequencia = 10;
    private const int MaximoDeTagsPorBandeira = 8;

    private static bool EhTomDePele(int codePoint) => codePoint is >= 0x1F3FB and <= 0x1F3FF;

    /// <summary>
    /// Se o texto tem emoji (base desenhada ou parte de sequência). Usado na
    /// senha: o mesmo coração vira "❤" num teclado e "❤️" em outro, e a pessoa
    /// não consegue mais entrar pelo outro aparelho.
    /// </summary>
    public static bool TemEmoji(string texto) =>
        texto.EnumerateRunes().Any(r => EhBaseDeEmoji(r.Value) || EhParteDeEmoji(r.Value));

    private static bool EhVisivel(Rune rune) =>
        !Rune.IsWhiteSpace(rune) && !EhInvisivelOuControle(rune) && !EhParteDeEmoji(rune.Value);

    private static bool ProximoEhBaseDeEmoji(string texto, int posicao) =>
        posicao < texto.Length
        && Rune.DecodeFromUtf16(texto.AsSpan(posicao), out var proximo, out _) == System.Buffers.OperationStatus.Done
        && EhBaseDeEmoji(proximo.Value);

    /// <summary>Limpa e valida um texto opcional: vazio (ou só espaços) vira string vazia.</summary>
    public static string ValidarOpcional(string? valor, RegraTexto regra)
    {
        var limpo = Limpar(valor, regra);
        if (limpo.Length > 0)
        {
            ConferirTamanho(limpo, regra);
        }
        return limpo;
    }

    /// <summary>
    /// Nome de pessoa: além do tamanho, precisa ter pelo menos 2 letras —
    /// "123", "..." ou "-- --" não são nome de ninguém.
    /// </summary>
    public static string ValidarNomePessoa(string? valor, RegraTexto? regra = null)
    {
        regra ??= NomePessoa;
        var nome = Validar(valor, regra);
        // Conta letras por caractere de verdade (code point), igual ao painel:
        // char.IsLetter olha metade de um par UTF-16 e não via letras fora do
        // BMP (ex.: "𝓐𝓷𝓪").
        if (nome.EnumerateRunes().Count(Rune.IsLetter) < 2)
        {
            throw new BusinessRuleException($"{regra.Rotulo} precisa ter pelo menos 2 letras.");
        }
        return nome;
    }

    /// <summary>
    /// Limpa o texto antes de validar o tamanho:
    ///  - campos de UMA linha (nome, assunto, departamento, cargo...): qualquer
    ///    caractere de controle ou invisível é recusado — inclusive \n, \r e
    ///    tab —, espaços repetidos (inclusive NBSP) viram um só e as pontas
    ///    são aparadas;
    ///  - campos de texto LONGO (MultiLinha: descrição da ausência, motivo,
    ///    mensagem e resposta ao RH...): \r\n e \r viram \n, espaços repetidos
    ///    viram um só, CADA LINHA é aparada (uma linha só de espaços fica
    ///    vazia), e só então as quebras seguidas são reduzidas a no máximo 2 —
    ///    sem aparar as linhas antes, "\n \n \n" passava como 3 quebras.
    ///    Qualquer outro controle ou invisível (tab, zero-width, RTL override...)
    ///    continua recusado.
    /// "Invisível ou de controle" = categorias Unicode Cc/Cf, separadores de
    /// linha/parágrafo (Zl/Zp), code points sem caractere atribuído (Cn) e
    /// não-caracteres, e os "brancos" de <see cref="EhInvisivelExtra"/>.
    ///
    /// Exceção (só em <see cref="RegraTexto.AceitaEmoji"/>: textos longos e
    /// assunto da mensagem): as partes de emoji são aceitas só onde formam
    /// uma sequência de emoji de verdade (ver <see cref="SequenciaDeEmoji"/>).
    /// Antes bastava vir depois de qualquer letra, e "Oi" + 1998 seletores ou
    /// "Oi" + um texto escondido em tags passavam com 2 letras visíveis.
    /// </summary>
    public static string Limpar(string? valor, RegraTexto regra)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return string.Empty;
        }

        // Um texto absurdamente maior que o permitido nem precisa ser
        // percorrido: depois da limpeza ele continuaria grande demais.
        if (regra.Maximo > 0 && valor.Length > (regra.Maximo * 4) + 1000)
        {
            throw new BusinessRuleException(MensagemMaximo(regra));
        }

        var mensagemInvisivel = $"{regra.Rotulo} contém caracteres invisíveis ou de controle, que não são permitidos.";
        var emoji = new SequenciaDeEmoji();
        var i = 0;
        while (i < valor.Length)
        {
            var quebraPermitida = regra.MultiLinha && valor[i] is '\n' or '\r';
            if (Rune.DecodeFromUtf16(valor.AsSpan(i), out var rune, out var consumidos) != System.Buffers.OperationStatus.Done)
            {
                throw new BusinessRuleException(mensagemInvisivel);
            }

            var aceito = regra.AceitaEmoji
                ? emoji.Aceitar(rune.Value, quebraPermitida, valor, i + consumidos)
                : quebraPermitida || !EhInvisivelOuControle(rune);
            if (!aceito)
            {
                throw new BusinessRuleException(mensagemInvisivel);
            }
            i += consumidos;
        }
        if (regra.AceitaEmoji && !emoji.Terminada)
        {
            // Bandeira com tags que não fecharam com U+E007F.
            throw new BusinessRuleException(mensagemInvisivel);
        }

        var texto = valor;
        if (regra.MultiLinha)
        {
            texto = texto.Replace("\r\n", "\n").Replace('\r', '\n');
        }
        texto = EspacosRepetidos().Replace(texto, " ");
        if (regra.MultiLinha)
        {
            texto = string.Join('\n', texto.Split('\n').Select(linha => linha.Trim(' ')));
            texto = QuebrasDemais().Replace(texto, "\n\n");
        }

        // Sobram só ' ' e '\n' como brancos: Trim tira os dois das pontas.
        return texto.Trim();
    }

    /// <summary>
    /// Se o texto tem algum caractere de controle ou invisível (Cc, Cf, Zl,
    /// Zp, surrogate solto, code point sem caractere ou não-caractere, ou os
    /// "brancos" de <see cref="EhInvisivelExtra"/>).
    /// Usado na senha, onde nem quebra de linha é aceita.
    /// </summary>
    public static bool TemInvisivelOuControle(string texto)
    {
        var i = 0;
        while (i < texto.Length)
        {
            if (Rune.DecodeFromUtf16(texto.AsSpan(i), out var rune, out var consumidos) != System.Buffers.OperationStatus.Done
                || EhInvisivelOuControle(rune))
            {
                return true;
            }
            i += consumidos;
        }
        return false;
    }

    private static bool EhInvisivelOuControle(Rune rune)
    {
        var categoria = Rune.GetUnicodeCategory(rune);
        return categoria is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
            || EhInvisivelExtra(rune.Value)
            || EhNaoCaractere(rune.Value)
            || (categoria == UnicodeCategory.OtherNotAssigned && !EhFaixaDeEmoji(rune.Value));
    }

    /// <summary>
    /// Não-caracteres do Unicode (U+FDD0–U+FDEF e todo U+xFFFE/U+xFFFF):
    /// reservados pra uso interno de programas, nunca são texto.
    /// </summary>
    private static bool EhNaoCaractere(int codePoint) =>
        codePoint is >= 0xFDD0 and <= 0xFDEF || (codePoint & 0xFFFE) == 0xFFFE;

    // Os code points ainda sem caractere (Cn) são recusados: vários deles,
    // como U+2065, U+FFF0–U+FFF8 e U+E0080–U+E0FFF, não aparecem na tela e
    // davam nomes "gêmeos". A exceção é a faixa dos emoji: os emoji novos
    // (Unicode 16 e 17, que os celulares já mandam) ainda são Cn pra tabela
    // do .NET 8, e recusá-los quebraria o "😮‍💨" de amanhã.
    private static bool EhFaixaDeEmoji(int codePoint) => codePoint is >= 0x1F000 and <= 0x1FAFF;

    /// <summary>
    /// Confere, caractere a caractere, se as partes de emoji de um texto
    /// formam sequências de emoji de verdade:
    ///  - seletor de variação (U+FE0E/U+FE0F): só depois de uma base de
    ///    emoji (<see cref="EhBaseDeEmoji"/>) ou de keycap (dígito, # ou *),
    ///    no máximo um por base;
    ///  - keycap (U+20E3): só depois de dígito, # ou * (com ou sem seletor);
    ///  - tom de pele (U+1F3FB–U+1F3FF): só depois de uma base de emoji,
    ///    no máximo um por base;
    ///  - ZWJ (U+200D): só ENTRE duas bases de emoji (antes dele pode vir o
    ///    seletor ou o tom da primeira), no máximo 10 por sequência;
    ///  - tags (U+E0020–U+E007E): só logo depois da bandeira 🏴 (U+1F3F4),
    ///    no máximo 8, e terminando em U+E007F.
    /// Qualquer outro caractere segue a regra comum (invisível ou de controle
    /// é recusado; a quebra de linha permitida passa).
    /// </summary>
    private sealed class SequenciaDeEmoji
    {
        private enum TipoBase { Nenhuma, Emoji, Keycap }

        private TipoBase _base = TipoBase.Nenhuma;
        private bool _seletorUsado;
        private bool _tomUsado;
        private bool _depoisDoZwj;
        private int _zwjsNaSequencia;
        private int _tags = -1;          // -1: fora de uma bandeira com tags
        private int _anterior = -1;

        /// <summary>Falso se o texto acabou no meio de uma bandeira com tags.</summary>
        public bool Terminada => _tags < 0;

        public bool Aceitar(int codePoint, bool quebraPermitida, string texto, int posicaoDoProximo)
        {
            var aceito = Avaliar(codePoint, quebraPermitida, texto, posicaoDoProximo);
            _anterior = codePoint;
            return aceito;
        }

        private bool Avaliar(int codePoint, bool quebraPermitida, string texto, int posicaoDoProximo)
        {
            if (_tags >= 0)
            {
                if (EhTag(codePoint))
                {
                    return ++_tags <= MaximoDeTagsPorBandeira;
                }
                if (codePoint == FimDasTags)
                {
                    // Bandeira fechada: nada mais se junta a ela.
                    _tags = -1;
                    _base = TipoBase.Nenhuma;
                    return true;
                }
                return false;
            }

            switch (codePoint)
            {
                case var _ when EhTag(codePoint):
                    if (_anterior != BandeiraPreta)
                    {
                        return false;
                    }
                    _tags = 1;
                    return true;

                case FimDasTags:
                    return false;

                case 0xFE0E or 0xFE0F:
                    if (_base == TipoBase.Nenhuma || _seletorUsado)
                    {
                        return false;
                    }
                    _seletorUsado = true;
                    return true;

                case 0x20E3:
                    if (_base != TipoBase.Keycap)
                    {
                        return false;
                    }
                    _base = TipoBase.Nenhuma;
                    return true;

                case var _ when EhTomDePele(codePoint):
                    if (_base != TipoBase.Emoji || _tomUsado)
                    {
                        return false;
                    }
                    _tomUsado = true;
                    return true;

                case 0x200D:
                    if (_base != TipoBase.Emoji
                        || !ProximoEhBaseDeEmoji(texto, posicaoDoProximo)
                        || ++_zwjsNaSequencia > MaximoDeZwjPorSequencia)
                    {
                        return false;
                    }
                    _base = TipoBase.Nenhuma;
                    _depoisDoZwj = true;
                    return true;
            }

            if (!quebraPermitida && EhInvisivelOuControle(new Rune(codePoint)))
            {
                return false;
            }

            // Caractere comum: começa uma base nova (ou nenhuma).
            if (!_depoisDoZwj)
            {
                _zwjsNaSequencia = 0;
            }
            _depoisDoZwj = false;
            _seletorUsado = false;
            _tomUsado = false;
            _base = EhBaseDeEmoji(codePoint) ? TipoBase.Emoji
                : EhBaseDeKeycap(codePoint) ? TipoBase.Keycap
                : TipoBase.Nenhuma;
            return true;
        }
    }

    // Espaço comum, NBSP e os outros separadores de espaço (categoria Zs).
    [GeneratedRegex(@"\p{Zs}+")]
    private static partial Regex EspacosRepetidos();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex QuebrasDemais();

    /// <summary>
    /// Tamanho contado em caracteres de verdade (code points), como na SPEC,
    /// no painel, no app e no varchar(N) do MySQL utf8mb4. Com string.Length
    /// (unidades UTF-16) um emoji fora do BMP contava 2: "99 letras + 👍"
    /// passava no app e era recusado aqui.
    /// O MÍNIMO não conta as partes de emoji (<see cref="EhParteDeEmoji"/>):
    /// "a" + 2 seletores tinha 3 code points e 1 caractere na tela. O MÁXIMO
    /// conta tudo, porque é o que ocupa a coluna no banco.
    /// </summary>
    private static void ConferirTamanho(string limpo, RegraTexto regra)
    {
        var caracteres = 0;
        var partesDeEmoji = 0;
        foreach (var rune in limpo.EnumerateRunes())
        {
            caracteres++;
            if (EhParteDeEmoji(rune.Value))
            {
                partesDeEmoji++;
            }
        }
        if (caracteres - partesDeEmoji < regra.Minimo)
        {
            throw new BusinessRuleException($"{regra.Rotulo} precisa ter pelo menos {regra.Minimo} caracteres.");
        }
        if (caracteres > regra.Maximo)
        {
            throw new BusinessRuleException(MensagemMaximo(regra));
        }
    }

    private static string MensagemMaximo(RegraTexto regra) =>
        $"{regra.Rotulo} pode ter no máximo {regra.Maximo} caracteres.";
}
