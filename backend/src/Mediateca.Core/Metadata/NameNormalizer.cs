using System.Text;

namespace Mediateca.Core.Metadata;

/// <summary>
/// Normaliza nombres para compararlos: minúsculas, sin acentos ni signos, y con los caracteres que se parecen a
/// una letra convertidos a esa letra (letras matemáticas 𝐔 y de ancho completo, numerales romanos Ⅲ y los
/// homoglifos cirílicos). No usa <c>string.Normalize</c>: depende de ICU y falla en el modo invariante de
/// globalización, igual que <c>SortTitles</c>.
/// </summary>
public static class NameNormalizer
{
    private static readonly string[] Romans = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"];

    // Letra sin acento y las letras acentuadas que se le parecen (con las vocales con macrón del japonés).
    private static readonly (char Plain, string Accented)[] AccentGroups =
    [
        ('a', "áàâãäåāăą"),
        ('c', "çćč"),
        ('d', "ď"),
        ('e', "éèêëēėęě"),
        ('i', "íìîïīįı"),
        ('l', "ł"),
        ('n', "ñńň"),
        ('o', "óòôõöøōő"),
        ('r', "ř"),
        ('s', "śšş"),
        ('t', "ť"),
        ('u', "úùûüūůűų"),
        ('y', "ýÿ"),
        ('z', "źżž"),
    ];

    // Letras cirílicas que se ven igual que una latina. Se cambian solo cuando el nombre es sobre todo latino:
    // "Juuоu" con una "о" cirílica es el mismo nombre; un título ruso se queda como está.
    private static readonly Dictionary<char, char> Homoglyphs = new()
    {
        ['а'] = 'a', ['е'] = 'e', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c', ['у'] = 'y', ['х'] = 'x',
        ['к'] = 'k', ['м'] = 'm', ['т'] = 't', ['н'] = 'h', ['в'] = 'b', ['і'] = 'i', ['ј'] = 'j', ['ѕ'] = 's',
    };

    private static readonly Dictionary<char, char> Accents = BuildAccents();

    /// <summary>
    /// "𝐔𝐫𝐮𝐰𝐚𝐬𝐡𝐢 no Yoi no Tsuki" da "uruwashi no yoi no tsuki"; "JoJo’s Bizarre Adventure" da "jojos bizarre adventure".
    /// Los apóstrofes se quitan (no dejan un espacio) y cualquier otro signo es un espacio.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var mapped = new StringBuilder(text.Length + 8);
        foreach (var rune in text.EnumerateRunes())
        {
            var codePoint = rune.Value;
            if (codePoint is >= 0x1D400 and <= 0x1D6A3)
            {
                // Letras matemáticas: bloques de 52 (A–Z y a–z) por cada estilo (negrita, cursiva, script…).
                var index = (codePoint - 0x1D400) % 52;
                mapped.Append((char)(index < 26 ? 'A' + index : 'a' + index - 26));
            }
            else if (codePoint is >= 0x1D7CE and <= 0x1D7FF)
            {
                mapped.Append((char)('0' + (codePoint - 0x1D7CE) % 10));
            }
            else if (codePoint is >= 0xFF01 and <= 0xFF5E)
            {
                // Ancho completo: ＡＢＣ１２３ son ASCII desplazado.
                mapped.Append((char)(codePoint - 0xFEE0));
            }
            else if (codePoint == 0x3000)
            {
                mapped.Append(' ');
            }
            else if (codePoint is >= 0x2160 and <= 0x216B)
            {
                mapped.Append(' ').Append(Romans[codePoint - 0x2160]).Append(' ');
            }
            else if (codePoint is >= 0x2170 and <= 0x217B)
            {
                mapped.Append(' ').Append(Romans[codePoint - 0x2170].ToLowerInvariant()).Append(' ');
            }
            else
            {
                mapped.Append(rune.ToString());
            }
        }

        var lower = mapped.ToString().ToLowerInvariant();

        var latin = 0;
        var cyrillic = 0;
        foreach (var character in lower)
        {
            if (character is >= 'a' and <= 'z')
            {
                latin++;
            }
            else if (Homoglyphs.ContainsKey(character))
            {
                cyrillic++;
            }
        }

        var fixCyrillic = cyrillic > 0 && latin > cyrillic;

        var result = new StringBuilder(lower.Length);
        var lastWasSpace = true;
        foreach (var rune in lower.EnumerateRunes())
        {
            if (rune.IsBmp)
            {
                var character = (char)rune.Value;
                if (Accents.TryGetValue(character, out var plain))
                {
                    character = plain;
                }

                if (fixCyrillic && Homoglyphs.TryGetValue(character, out var latinCharacter))
                {
                    character = latinCharacter;
                }

                if (character is '\'' or '’' or '‘' or '`' or '´' or 'ʼ')
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    result.Append(character);
                    lastWasSpace = false;
                    continue;
                }
            }
            else if (Rune.IsLetterOrDigit(rune))
            {
                result.Append(rune.ToString());
                lastWasSpace = false;
                continue;
            }

            if (!lastWasSpace)
            {
                result.Append(' ');
                lastWasSpace = true;
            }
        }

        return result.ToString().Trim();
    }

    /// <summary>
    /// Parecido entre dos nombres ya normalizados, de 0 a 1: 1 si son iguales, 0.98 si solo cambian los espacios
    /// ("rezero" y "re zero") y, si no, el promedio del parecido por pares de letras y por palabras; un nombre
    /// que contiene entero al otro vale al menos 0.55 más su proporción de largo.
    /// </summary>
    public static double Similarity(string? first, string? second)
    {
        if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
        {
            return 0;
        }

        if (first == second)
        {
            return 1;
        }

        var compactFirst = first.Replace(" ", "");
        var compactSecond = second.Replace(" ", "");
        if (compactFirst == compactSecond)
        {
            return 0.98;
        }

        var wordsFirst = first.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var wordsSecond = second.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var shared = wordsFirst.Count(wordsSecond.Contains);
        var wordScore = 2.0 * shared / (wordsFirst.Count + wordsSecond.Count);

        var score = 0.5 * Dice(Bigrams(compactFirst), Bigrams(compactSecond)) + 0.5 * wordScore;

        var (shorter, longer) = compactFirst.Length <= compactSecond.Length
            ? (compactFirst, compactSecond)
            : (compactSecond, compactFirst);
        if (shorter.Length >= 4 && longer.Contains(shorter, StringComparison.Ordinal))
        {
            score = Math.Max(score, 0.55 + 0.4 * shorter.Length / longer.Length);
        }

        return Math.Min(score, 1);
    }

    private static List<string> Bigrams(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        if (text.Length == 1)
        {
            return [text];
        }

        var bigrams = new List<string>(text.Length - 1);
        for (var i = 0; i < text.Length - 1; i++)
        {
            bigrams.Add(text.Substring(i, 2));
        }

        return bigrams;
    }

    // Coeficiente de Dice entre dos multiconjuntos: 2 × lo que comparten ÷ el total.
    private static double Dice(List<string> first, List<string> second)
    {
        if (first.Count == 0 || second.Count == 0)
        {
            return 0;
        }

        var counts = new Dictionary<string, int>();
        foreach (var item in first)
        {
            counts[item] = counts.GetValueOrDefault(item) + 1;
        }

        var shared = 0;
        foreach (var item in second)
        {
            if (counts.TryGetValue(item, out var left) && left > 0)
            {
                counts[item] = left - 1;
                shared++;
            }
        }

        return 2.0 * shared / (first.Count + second.Count);
    }

    private static Dictionary<char, char> BuildAccents()
    {
        var map = new Dictionary<char, char>();
        foreach (var (plain, accented) in AccentGroups)
        {
            foreach (var character in accented)
            {
                map[character] = plain;
            }
        }

        return map;
    }
}
