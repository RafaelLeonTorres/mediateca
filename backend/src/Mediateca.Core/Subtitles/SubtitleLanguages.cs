using System.Globalization;
using System.Text;

namespace Mediateca.Core.Subtitles;

/// <summary>
/// Idiomas de los subtítulos (PLAN.md, sección 9). Acepta códigos de 2 y 3 letras (<c>es</c>, <c>spa</c>), con
/// región (<c>es-MX</c>, <c>pt_BR</c>) y nombres en inglés o español (<c>Spanish</c>, <c>español</c>), sin
/// importar mayúsculas ni acentos. Los idiomas conocidos se normalizan al código de 2 letras; los demás se
/// conservan tal cual (en minúsculas) para no perder lo que dice el archivo.
/// </summary>
public static class SubtitleLanguages
{
    /// <summary>Código de 2 letras → nombre en español, y todas las formas con las que se reconoce.</summary>
    private static readonly (string Code, string Name, string[] Aliases)[] Known =
    [
        ("es", "Español", ["es", "spa", "esp", "spanish", "espanol", "castellano", "castilian", "latino", "latam", "lat"]),
        ("en", "Inglés", ["en", "eng", "english", "ingles"]),
        ("ja", "Japonés", ["ja", "jpn", "jap", "japanese", "japones"]),
        ("pt", "Portugués", ["pt", "por", "ptb", "portuguese", "portugues"]),
        ("fr", "Francés", ["fr", "fre", "fra", "french", "frances"]),
        ("de", "Alemán", ["de", "ger", "deu", "german", "aleman"]),
        ("it", "Italiano", ["it", "ita", "italian", "italiano"]),
        ("zh", "Chino", ["zh", "chi", "zho", "chs", "cht", "chinese", "chino"]),
        ("ko", "Coreano", ["ko", "kor", "korean", "coreano"]),
    ];

    private static readonly Dictionary<string, string> CodeByAlias = Known
        .SelectMany(language => language.Aliases.Select(alias => (alias, language.Code)))
        .ToDictionary(pair => pair.alias, pair => pair.Code, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> NameByCode = Known
        .ToDictionary(language => language.Code, language => language.Name, StringComparer.Ordinal);

    /// <summary>Valores que significan "sin idioma".</summary>
    private static readonly HashSet<string> Undetermined =
        new(StringComparer.Ordinal) { "und", "mis", "mul", "zxx", "unknown", "desconocido" };

    /// <summary>
    /// Código normalizado de un idioma conocido (<c>es</c>, <c>en</c>...), o null si la palabra no es uno de
    /// los idiomas que reconoce la app. Sirve para leer los segmentos del nombre de un archivo suelto.
    /// </summary>
    public static string? TryGetKnown(string? raw)
    {
        var key = Fold(raw);
        if (key.Length == 0)
        {
            return null;
        }

        if (CodeByAlias.TryGetValue(key, out var code))
        {
            return code;
        }

        // Con región o variante: es-MX, pt_BR, zh-Hans.
        var separator = key.IndexOfAny(['-', '_']);
        return separator > 0 && CodeByAlias.TryGetValue(key[..separator], out code) ? code : null;
    }

    /// <summary>
    /// Idioma de una pista incrustada: el código de 2 letras si se conoce, el valor en minúsculas si no,
    /// y null si no hay idioma (vacío o "und").
    /// </summary>
    public static string? Normalize(string? raw)
    {
        var key = Fold(raw);
        if (key.Length == 0 || Undetermined.Contains(key))
        {
            return null;
        }

        return TryGetKnown(key) ?? key;
    }

    /// <summary>Nombre en español para mostrar: "Español", "Inglés"... Los idiomas desconocidos se muestran en mayúsculas.</summary>
    public static string NameOf(string code) =>
        NameByCode.TryGetValue(code, out var name) ? name : code.ToUpperInvariant();

    /// <summary>Minúsculas, sin acentos y sin espacios en los extremos.</summary>
    private static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
