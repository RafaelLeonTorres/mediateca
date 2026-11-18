using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mediateca.Core.Media;
using Mediateca.Core.Metadata;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// Traduce las respuestas de OMDb (JSON plano, con "N/A" donde falta un dato) a <see cref="ProviderMetadata"/>,
/// <see cref="SearchCandidate"/> y títulos de episodios. Funciones puras, probadas con respuestas de ejemplo.
/// </summary>
public static class OmdbParser
{
    // "2019", "2019–2020" (con raya) y "2019–" (sigue en emisión).
    private static readonly Regex YearPattern = new(
        @"^\s*(\d{4})\s*(?:([–—-])\s*(\d{4})?)?\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex MinutesPattern = new(@"(\d+)\s*min", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> NoRating = new(StringComparer.OrdinalIgnoreCase)
    {
        "N/A", "Not Rated", "Unrated", "Not rated", "Unrated Version",
    };

    /// <summary>
    /// El error de una respuesta (<c>"Response": "False"</c>), por ejemplo "Movie not found!" o "Request limit reached!".
    /// Null si la respuesta es correcta.
    /// </summary>
    public static string? GetError(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!string.Equals(Str(root, "Response"), "False", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Str(root, "Error") ?? "Error desconocido";
    }

    /// <summary>OMDb dejó de responder por el límite diario ("Request limit reached!").</summary>
    public static bool IsDailyLimit(string? error) =>
        error is not null && error.Contains("limit reached", StringComparison.OrdinalIgnoreCase);

    public static bool IsInvalidKey(string? error) =>
        error is not null &&
        (error.Contains("Invalid API key", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("No API key", StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- búsqueda

    public static IReadOnlyList<SearchCandidate> ParseSearch(string json)
    {
        using var document = JsonDocument.Parse(json);
        var candidates = new List<SearchCandidate>();
        if (!document.RootElement.TryGetProperty("Search", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return candidates;
        }

        foreach (var result in results.EnumerateArray())
        {
            var id = Clean(Str(result, "imdbID"));
            var title = Clean(Str(result, "Title"));
            if (id is null || title is null)
            {
                continue;
            }

            var (year, endYear, _) = ParseYears(Clean(Str(result, "Year")));
            candidates.Add(new SearchCandidate(
                ExternalId: id,
                Title: title,
                OriginalTitle: null,
                Titles: [title],
                Year: year,
                EndYear: endYear,
                Status: null,
                Format: Clean(Str(result, "Type")),
                PosterUrl: PosterOf(result),
                Url: $"https://www.imdb.com/title/{id}/"));
        }

        return candidates;
    }

    // ---------------------------------------------------------------- detalle

    public static ProviderMetadata ParseDetails(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var id = Clean(Str(root, "imdbID")) ?? throw new MetadataProviderException("OMDb respondió sin el id de IMDb.");
        var title = Clean(Str(root, "Title")) ?? id;
        var type = Clean(Str(root, "Type"));
        var (year, endYear, airing) = ParseYears(Clean(Str(root, "Year")));

        ReleaseStatus? status = null;
        if (type == "series")
        {
            status = airing ? ReleaseStatus.Airing : endYear is not null ? ReleaseStatus.Finished : null;
        }

        double? rating = double.TryParse(
            Clean(Str(root, "imdbRating")), NumberStyles.Float, CultureInfo.InvariantCulture, out var imdbRating) && imdbRating > 0
            ? imdbRating
            : null;

        var rated = Clean(Str(root, "Rated"));
        var runtime = Clean(Str(root, "Runtime")) is { } runtimeText && MinutesPattern.Match(runtimeText) is { Success: true } minutes
            ? int.Parse(minutes.Groups[1].Value, CultureInfo.InvariantCulture)
            : (int?)null;

        int? totalSeasons = int.TryParse(
            Clean(Str(root, "totalSeasons")), NumberStyles.None, CultureInfo.InvariantCulture, out var seasons)
            ? seasons
            : null;

        return new ProviderMetadata(
            Provider: MetadataProviders.Omdb,
            ExternalId: id,
            Title: title,
            OriginalTitle: null,
            Year: year,
            EndYear: endYear,
            Status: status,
            Overview: Clean(Str(root, "Plot")),
            ContentRating: rated is null || NoRating.Contains(rated) ? null : rated,
            CommunityRating: rating,
            Genres: List(Str(root, "Genre")),
            Tags: [],
            Studios: List(Str(root, "Production")),
            Directors: List(Str(root, "Director")),
            Actors: List(Str(root, "Actors")),
            RuntimeMinutes: runtime,
            Format: type,
            Episodes: null,
            PosterUrl: PosterOf(root),
            BackdropUrl: null,
            NextAiringAt: null,
            NextAiringEpisode: null,
            Links: [new ExternalLink("IMDb", $"https://www.imdb.com/title/{id}/")],
            ImdbId: id,
            AniListId: null,
            EpisodeTitles: [],
            TotalSeasons: totalSeasons);
    }

    // ---------------------------------------------------------------- episodios

    /// <summary>Los episodios de una temporada (<c>?i=tt…&amp;Season=N</c>). Los que no traen título se omiten.</summary>
    public static IReadOnlyList<EpisodeTitle> ParseSeason(string json, int season)
    {
        using var document = JsonDocument.Parse(json);
        var titles = new List<EpisodeTitle>();
        if (!document.RootElement.TryGetProperty("Episodes", out var episodes) || episodes.ValueKind != JsonValueKind.Array)
        {
            return titles;
        }

        foreach (var episode in episodes.EnumerateArray())
        {
            if (int.TryParse(Clean(Str(episode, "Episode")), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                Clean(Str(episode, "Title")) is { } title)
            {
                titles.Add(new EpisodeTitle(season, number, title));
            }
        }

        return titles;
    }

    // ---------------------------------------------------------------- partes

    /// <summary>Año de inicio, año de fin y si "sigue" (la raya sin año final: "2019–").</summary>
    public static (int? Year, int? EndYear, bool Ongoing) ParseYears(string? text)
    {
        if (text is null)
        {
            return (null, null, false);
        }

        var match = YearPattern.Match(text);
        if (!match.Success)
        {
            return (null, null, false);
        }

        var start = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (!match.Groups[2].Success)
        {
            return (start, null, false);
        }

        return match.Groups[3].Success
            ? (start, int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture), false)
            : (start, null, true);
    }

    private static string? PosterOf(JsonElement element) =>
        Clean(Str(element, "Poster")) is { } poster && Uri.TryCreate(poster, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            ? poster
            : null;

    private static List<string> List(string? value) =>
        Clean(value) is { } text
            ? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => item != "N/A")
                .ToList()
            : [];

    // OMDb escribe "N/A" cuando no tiene el dato.
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "N/A" ? null : value.Trim();

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
