using System.Globalization;
using System.Text.RegularExpressions;

namespace Mediateca.Core.Metadata;

/// <summary>
/// Lo que se saca del nombre de una carpeta o de un título para buscarlo: el nombre limpio y, aparte, las pistas
/// para puntuar. <c>Title</c> ya está normalizado (minúsculas, sin acentos ni signos) y sin temporada, año ni
/// "movie"; <c>Alternates</c> son los nombres entre paréntesis ("(The Ramparts of Ice Season 2)"); <c>Season</c>
/// y <c>Part</c> son la temporada y el cour o parte; <c>IsMovie</c> dice que el nombre habla de una película.
/// </summary>
public sealed record NameHint(
    string Title,
    IReadOnlyList<string> Alternates,
    int? Season,
    int? Part,
    int? Year,
    bool IsMovie)
{
    /// <summary>Todos los nombres con los que se compara, el principal primero.</summary>
    public IEnumerable<string> AllTitles => new[] { Title }.Concat(Alternates).Where(title => title.Length > 0);
}

/// <summary>
/// Limpia el nombre de una carpeta antes de buscarlo (PLAN.md, sección 8). Quita el año entre paréntesis, la
/// temporada ("Season 3", "seasson 2", "2nd Season", "second seasson", "Temporada 3", "S2", números romanos), el
/// cour o la parte ("Cour 2", "2nd Cour", "Part 2") y la palabra "Movie", y los deja como pistas aparte. También
/// sirve para los títulos de los resultados: así "Frieren 2nd Season" y la carpeta "Frieren season 2" se comparan
/// por "frieren" y por temporada 2.
/// </summary>
public static class SearchNameCleaner
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string SeasonWord = @"(?:seas+on|sason|saison|temporada|temp)";

    private static readonly Dictionary<string, int> Ordinals = new()
    {
        ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5, ["sixth"] = 6,
        ["seventh"] = 7, ["eighth"] = 8, ["ninth"] = 9, ["tenth"] = 10,
    };

    // Solo los de dos o más letras que no son una palabra de verdad: "i", "v" y "x" se quedan en el título.
    private static readonly Dictionary<string, int> RomanSeasons = new()
    {
        ["ii"] = 2, ["iii"] = 3, ["iv"] = 4, ["vi"] = 6, ["vii"] = 7, ["viii"] = 8, ["ix"] = 9,
    };

    private static readonly Regex Brackets = new(@"\[[^\]]*\]", Options);

    // "(2026)", "(The Ramparts of Ice Season 2)" y también un paréntesis que nunca se cierra.
    private static readonly Regex Parentheses = new(@"\(([^()]*)(?:\)|$)", Options);

    private static readonly Regex YearOnly = new(@"^(?:19|20)[0-9]{2}$", Options);

    private static readonly Regex NumberedSeasonWord = new(
        @"\b(\d{1,2})(?:st|nd|rd|th)\s*(" + SeasonWord + @"|cour|part)\b", Options);

    private static readonly Regex OrdinalWord = new(
        @"\b(" + string.Join("|", Ordinals.Keys) + @")\s*(" + SeasonWord + @"|cour|part)\b", Options);

    private static readonly Regex SeasonNumber = new(@"\b" + SeasonWord + @"\s*(\d{1,2})\b", Options);

    private static readonly Regex CourNumber = new(@"\b(cour|part|parte)\s*(\d{1,2})\b", Options);

    private static readonly Regex ShortSeason = new(@"\bs(\d{1,2})\b", Options);

    private static readonly Regex MovieWord = new(
        @"\b(?:the\s+)?(?:movie|pelicula|gekijouban)(?:\s*\d{1,2})?\b", Options);

    private static readonly Regex FinalSeason = new(@"\bfinal\s*" + SeasonWord + @"\b", Options);

    private static readonly Regex StartsWithSeasonWord = new(@"^" + SeasonWord, Options);

    private static readonly Regex Spaces = new(@"\s+", Options);

    private sealed record Markers(string Title, int? Season, int? Part, bool Movie, int? Year);

    /// <summary>Limpia un nombre de carpeta o un título. Nunca devuelve null; un nombre vacío da un título vacío.</summary>
    public static NameHint Clean(string? raw)
    {
        var text = Brackets.Replace(raw ?? "", " ");
        var alternates = new List<string>();
        int? year = null;

        text = Parentheses.Replace(text, match =>
        {
            var inner = match.Groups[1].Value.Trim();
            if (YearOnly.IsMatch(inner))
            {
                year = int.Parse(inner, CultureInfo.InvariantCulture);
            }
            else if (inner.Count(char.IsLetter) >= 2)
            {
                alternates.Add(inner);
            }

            return " ";
        });

        var main = ExtractMarkers(NameNormalizer.Normalize(text));
        var season = main.Season;
        var part = main.Part;
        var movie = main.Movie;
        year ??= main.Year;

        var alternateTitles = new List<string>();
        foreach (var alternate in alternates)
        {
            var markers = ExtractMarkers(NameNormalizer.Normalize(alternate));
            if (markers.Title.Length > 0 && markers.Title != main.Title)
            {
                alternateTitles.Add(markers.Title);
            }

            season ??= markers.Season;
            part ??= markers.Part;
            movie |= markers.Movie;
            year ??= markers.Year;
        }

        return new NameHint(main.Title, alternateTitles, season, part, year, movie);
    }

    private static Markers ExtractMarkers(string normalized)
    {
        var text = normalized;
        int? season = null;
        int? part = null;
        var movie = false;
        int? year = null;

        text = NumberedSeasonWord.Replace(text, match =>
        {
            var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (StartsWithSeasonWord.IsMatch(match.Groups[2].Value))
            {
                season ??= number;
            }
            else
            {
                part ??= number;
            }

            return " ";
        });

        text = OrdinalWord.Replace(text, match =>
        {
            var number = Ordinals[match.Groups[1].Value];
            if (StartsWithSeasonWord.IsMatch(match.Groups[2].Value))
            {
                season ??= number;
            }
            else
            {
                part ??= number;
            }

            return " ";
        });

        text = SeasonNumber.Replace(text, match =>
        {
            season ??= int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return " ";
        });

        text = CourNumber.Replace(text, match =>
        {
            part ??= int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            return " ";
        });

        text = ShortSeason.Replace(text, match =>
        {
            season ??= int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return " ";
        });

        text = MovieWord.Replace(text, _ =>
        {
            movie = true;
            return " ";
        });

        text = FinalSeason.Replace(text, " ");
        text = Spaces.Replace(text, " ").Trim();

        // Palabra por palabra: los numerales romanos y el año no pueden ser la primera (un título que empieza
        // con "2012" o con "II" no tiene temporada ni año).
        var words = text.Length == 0 ? Array.Empty<string>() : text.Split(' ');
        var kept = new List<string>(words.Length);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            if (i > 0 && RomanSeasons.TryGetValue(word, out var romanSeason))
            {
                season ??= romanSeason;
                continue;
            }

            if (i > 0 && word.Length == 4 && YearOnly.IsMatch(word))
            {
                year ??= int.Parse(word, CultureInfo.InvariantCulture);
                continue;
            }

            kept.Add(word);
        }

        return new Markers(string.Join(' ', kept), season, part, movie, year);
    }
}
