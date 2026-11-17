using System.Text;
using System.Text.RegularExpressions;

namespace Mediateca.Core.Naming;

/// <summary>Título para ordenar: minúsculas, sin acentos y sin el artículo inicial.</summary>
public static class SortTitles
{
    private const int MaxLength = 500;

    private static readonly string[] Articles = ["el", "la", "los", "las", "un", "una", "the", "a", "an"];

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    // Letra sin acento y las letras acentuadas que se le parecen. No depende de ICU ni de la
    // normalización de Unicode, que no están disponibles en el modo invariante de globalización.
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

    private static readonly Dictionary<char, char> AccentMap = BuildAccentMap();

    /// <summary>"The Matrix" da "matrix"; "El Señor de los Anillos" da "senor de los anillos".</summary>
    public static string From(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var lower = title.Trim().ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);
        foreach (var character in lower)
        {
            builder.Append(AccentMap.GetValueOrDefault(character, character));
        }

        var sortTitle = Spaces.Replace(builder.ToString(), " ").Trim();
        foreach (var article in Articles)
        {
            // El artículo solo se quita si queda algo después ("The" a secas se queda igual).
            if (sortTitle.Length > article.Length + 1 &&
                sortTitle.StartsWith(article + " ", StringComparison.Ordinal))
            {
                sortTitle = sortTitle[(article.Length + 1)..].TrimStart();
                break;
            }
        }

        return sortTitle.Length > MaxLength ? sortTitle[..MaxLength] : sortTitle;
    }

    private static Dictionary<char, char> BuildAccentMap()
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
