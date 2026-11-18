using System.Globalization;
using System.Text.RegularExpressions;

namespace Mediateca.Core.Naming;

/// <summary>
/// Datos de un episodio que salen de su ruta. Con <c>SxxEyy</c> hay temporada y episodio; con el
/// número suelto del anime ("Serie - 05", "01.mp4") y sin carpeta de temporada, solo el número absoluto.
/// <c>Group</c> es la carpeta dentro de la serie que no es de temporada ("Lat", "Sub"), tal como se
/// llama en el disco; si hay varias, van unidas con " / ".
/// </summary>
public sealed record ParsedEpisode(
    string SeriesTitle,
    int? SeriesYear,
    int? SeasonNumber,
    int? EpisodeNumber,
    int? AbsoluteNumber,
    string? EpisodeTitle,
    string? ImdbId,
    int? AniListId,
    string? Group = null);

/// <summary>
/// Series (<c>Serie (Año)/Temporada NN/… SxxEyy</c>) y anime (<c>Serie/[Grupo] Serie - 05 [1080p]</c>,
/// <c>Serie/Lat/01.mp4</c>). Recibe la ruta relativa a la carpeta de la biblioteca. La primera carpeta
/// es la serie, salvo que sea una carpeta de temporada; si el archivo está suelto, el título sale del
/// nombre. Las demás carpetas dentro de la serie (por ejemplo, el idioma) forman un grupo.
/// </summary>
public static class EpisodeNameParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex SeasonFolder = new(
        @"^(?:temporada|temp|season|series|s)[\s._-]*([0-9]{1,3})$", Options);

    private static readonly Regex SpecialsFolder = new(@"^(?:especiales?|specials?)$", Options);

    // S01E02, s1e2, y los dobles: S01E01E02, S01E01-E02 y S01E01-02 (se conserva el primero).
    private static readonly Regex SeasonEpisode = new(
        @"(?<![\p{L}\p{N}])s([0-9]{1,2})[ ._-]?e([0-9]{1,4})(?:[ ._-]?e[0-9]{1,4}|-e?[0-9]{1,3}(?![0-9pP]))*",
        Options);

    // 1x02. El lookbehind evita tomar "1920x1080" por temporada y episodio.
    private static readonly Regex CrossNotation = new(
        @"(?<![\p{L}\p{N}])([0-9]{1,2})x([0-9]{1,3})(?![0-9])(?:-[0-9]{1,3}(?![0-9pP]))?", Options);

    // "Serie - 05", "Serie - 05v2" y "Serie - 05-06". "- 1080p" no cuenta: después del número
    // tiene que haber un espacio o el final.
    private static readonly Regex Dash = new(
        @"\s-\s([0-9]{1,4})(?:v[0-9]+)?(?:-[0-9]{1,4}(?:v[0-9]+)?)?(?=\s|$)", Options);

    private static readonly Regex EpisodeWord = new(
        @"(?<![\p{L}])(?:episodio|episode|capitulo|capítulo|ep|cap)\.?\s*([0-9]{1,4})(?:v[0-9]+)?(?![\p{L}0-9])",
        Options);

    private static readonly Regex Leading = new(@"^([0-9]{1,3})(?:v[0-9]+)?(?=[\s.\-_]|$)", Options);

    private static readonly Regex Trailing = new(@"\s([0-9]{1,4})(?:v[0-9]+)?$", Options);

    // El nombre es solo el número: "01.mp4", "05v2.mkv".
    private static readonly Regex Bare = new(@"^([0-9]{1,4})(?:v[0-9]+)?$", Options);

    // Especiales: "Especial01", "Special 2", "SP03", "OVA 1". Van a la temporada 0.
    private static readonly Regex Special = new(
        @"(?<![\p{L}\p{N}])(?:especial(?:es)?|specials?|sp|ova|oad)[\s._-]*([0-9]{1,3})(?:v[0-9]+)?(?![0-9])",
        Options);

    // Paréntesis de calidad o códec "(1080p x265)", pero no el año "(2019)".
    private static readonly Regex QualityParens = new(@"\((?!\s*(?:19|20)[0-9]{2}\s*\))[^)]*\)", Options);

    /// <summary>Serie: acepta SxxEyy y 1x02; con carpeta de temporada, también "05 - Título".</summary>
    public static ParsedEpisode ParseSeries(string relativePath) => Parse(relativePath, anime: false);

    /// <summary>Anime: además de SxxEyy acepta el número absoluto ("Serie - 05", "05v2").</summary>
    public static ParsedEpisode ParseAnime(string relativePath) => Parse(relativePath, anime: true);

    /// <summary>
    /// Número de temporada de una carpeta: "Temporada 01", "Season 2" o "S03". "Especiales" y
    /// "Specials" son la temporada 0. Devuelve null si no es una carpeta de temporada.
    /// </summary>
    public static int? ParseSeasonFolder(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);

        var name = folderName.Trim();
        var season = SeasonFolder.Match(name);
        if (season.Success)
        {
            return int.Parse(season.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        return SpecialsFolder.IsMatch(name) ? 0 : null;
    }

    private static ParsedEpisode Parse(string relativePath, bool anime)
    {
        var segments = PathSegments.Split(relativePath);
        var fileStem = PathSegments.Stem(segments[^1]);

        string? seriesFolder = null;
        int? folderSeason = null;
        var innerFolders = new List<string>();
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var season = ParseSeasonFolder(segments[i]);
            if (i == 0 && season is null)
            {
                seriesFolder = segments[i];
                continue;
            }

            if (season is not null)
            {
                folderSeason ??= season;
            }

            if (seriesFolder is not null)
            {
                innerFolders.Add(segments[i].Trim());
            }
        }

        // Una sola carpeta de temporada es la temporada de siempre. Cualquier otra carpeta dentro de
        // la serie ("Lat", "Sub", "Extras") se muestra como está, con sus episodios dentro.
        var group = innerFolders.Count == 0 || (innerFolders.Count == 1 && folderSeason is not null)
            ? null
            : string.Join(" / ", innerFolders);

        var file = FolderTags.Extract(fileStem);
        var folder = seriesFolder is null ? null : FolderTags.Extract(seriesFolder);

        // Lo que está entre corchetes (grupo, calidad, CRC) no cuenta; en anime, los paréntesis de
        // calidad tampoco.
        var text = TitleParsing.Brackets.Replace(file.Name, " ");
        if (anime)
        {
            text = QualityParens.Replace(text, " ");
        }

        text = TitleParsing.Collapse(TitleParsing.NormalizeSeparators(text));

        var hit = FindHit(text, folderSeason, anime);

        var titleSource = folder is not null
            ? folder.Name
            : hit is null ? text : hit.Leading ? "" : text[..hit.Start];
        var series = TitleParsing.ParseTitleAndYear(titleSource);

        string? episodeTitle = null;
        if (hit is not null)
        {
            var after = TitleParsing.Collapse(TitleParsing.TrimEdges(TitleParsing.CutRelease(text[hit.End..])));
            if (after.Any(char.IsLetter))
            {
                episodeTitle = after;
            }
        }

        return new ParsedEpisode(
            series.Title.Length > 0 ? series.Title : "Sin título",
            series.Year,
            hit?.Season ?? folderSeason,
            hit?.Episode,
            hit?.Absolute,
            episodeTitle,
            folder?.ImdbId ?? file.ImdbId,
            folder?.AniListId ?? file.AniListId,
            group);
    }

    /// <summary>Dónde está el número del episodio en el nombre y qué numeración es.</summary>
    private sealed record Hit(int Start, int End, int? Season, int? Episode, int? Absolute, bool Leading);

    private static Hit? FindHit(string text, int? folderSeason, bool anime)
    {
        var seasonEpisode = SeasonEpisode.Match(text);
        if (seasonEpisode.Success)
        {
            return new Hit(
                seasonEpisode.Index, seasonEpisode.Index + seasonEpisode.Length,
                Number(seasonEpisode.Groups[1]), Number(seasonEpisode.Groups[2]), null, false);
        }

        var cross = CrossNotation.Match(text);
        if (cross.Success)
        {
            return new Hit(
                cross.Index, cross.Index + cross.Length,
                Number(cross.Groups[1]), Number(cross.Groups[2]), null, false);
        }

        var special = Special.Match(text);
        if (special.Success)
        {
            return new Hit(
                special.Index, special.Index + special.Length,
                0, Number(special.Groups[1]), null, special.Index == 0);
        }

        if (anime || folderSeason is not null)
        {
            // Con varios " - NN" gana el último: "Fate - Zero - 05".
            Match? last = null;
            foreach (Match match in Dash.Matches(text))
            {
                if (IsPlausible(match.Groups[1]))
                {
                    last = match;
                }
            }

            if (last is not null)
            {
                return Numbered(last, folderSeason, leading: false);
            }
        }

        var word = EpisodeWord.Match(text);
        if (word.Success && IsPlausible(word.Groups[1]))
        {
            return Numbered(word, folderSeason, leading: false);
        }

        if (anime)
        {
            var trailing = Trailing.Match(text);
            if (trailing.Success && IsPlausible(trailing.Groups[1]))
            {
                return Numbered(trailing, folderSeason, leading: false);
            }

            // Un número al inicio seguido de texto puede ser parte del título ("86 Eighty Six"), pero
            // un nombre que es solo el número es el episodio.
            var bare = Bare.Match(text);
            if (bare.Success && IsPlausible(bare.Groups[1]))
            {
                return Numbered(bare, folderSeason, leading: true);
            }
        }

        if (folderSeason is not null || !anime)
        {
            var leading = Leading.Match(text);
            if (leading.Success && IsPlausible(leading.Groups[1]))
            {
                return Numbered(leading, folderSeason, leading: true);
            }
        }

        return null;
    }

    // Con carpeta de temporada, el número es el del episodio dentro de ella; sin ella, es absoluto.
    private static Hit Numbered(Match match, int? folderSeason, bool leading)
    {
        var number = Number(match.Groups[1]);
        var end = match.Index + match.Length;
        return folderSeason is { } season
            ? new Hit(match.Index, end, season, number, null, leading)
            : new Hit(match.Index, end, null, null, number, leading);
    }

    private static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

    // Un número de cuatro cifras entre 1900 y 2099 es un año, no un episodio.
    private static bool IsPlausible(Group group)
    {
        var number = Number(group);
        return !(group.Value.Length == 4 && number is >= 1900 and <= 2099);
    }
}
