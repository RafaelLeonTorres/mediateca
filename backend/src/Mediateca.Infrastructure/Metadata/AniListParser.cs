using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mediateca.Core.Media;
using Mediateca.Core.Metadata;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// Traduce las respuestas de AniList (JSON de GraphQL) a <see cref="ProviderMetadata"/> y <see cref="SearchCandidate"/>.
/// Son funciones puras: se prueban con respuestas de ejemplo, sin red.
/// </summary>
public static class AniListParser
{
    private const int MaxTags = 10;
    private const int MaxStudios = 8;

    // "Episode 3 - Título", "Ep. 3: Título", "E3 - Título". Sin un título después del número no sirve.
    private static readonly Regex EpisodeTitlePattern = new(
        @"^\s*(?:episodio|episode|ep\.?|e)\s*(\d{1,4})\s*[-–—:.]\s*(\S.*?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // ---------------------------------------------------------------- respuesta de la API

    /// <summary>El JSON de <c>data.Media</c> de una respuesta, o null si no hay (el id no existe).</summary>
    public static string? ExtractMedia(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        return Child(Child(document.RootElement, "data"), "Media") is { ValueKind: JsonValueKind.Object } media
            ? media.GetRawText()
            : null;
    }

    /// <summary>El arreglo JSON de <c>data.Page.media</c> de una respuesta de búsqueda, o null si no hay.</summary>
    public static string? ExtractSearch(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        return Child(Child(Child(document.RootElement, "data"), "Page"), "media") is { ValueKind: JsonValueKind.Array } media
            ? media.GetRawText()
            : null;
    }

    /// <summary>El primer error de la respuesta (<c>errors[0]</c>) con su código, o null si no trae errores.</summary>
    public static (int? Status, string Message)? ExtractError(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (Child(document.RootElement, "errors") is not { ValueKind: JsonValueKind.Array } errors || errors.GetArrayLength() == 0)
        {
            return null;
        }

        var first = errors[0];
        return (Int(first, "status"), Str(first, "message") ?? "error desconocido");
    }

    // ---------------------------------------------------------------- detalle

    public static ProviderMetadata ParseMedia(string mediaJson)
    {
        using var document = JsonDocument.Parse(mediaJson);
        return ParseMedia(document.RootElement);
    }

    public static ProviderMetadata ParseMedia(JsonElement media)
    {
        var id = Int(media, "id") ?? throw new MetadataProviderException("AniList respondió sin el id del anime.");

        var title = Child(media, "title");
        var romaji = Str(title, "romaji");
        var english = Str(title, "english");
        var native = Str(title, "native");

        var display = english ?? romaji ?? native ?? $"AniList {id}";
        // El título original es el romaji; si el título ya es el romaji (no hay inglés), el japonés.
        var original = romaji is not null && romaji != display ? romaji
            : native is not null && native != display ? native
            : null;

        var status = Str(media, "status") switch
        {
            "RELEASING" => ReleaseStatus.Airing,
            "FINISHED" => ReleaseStatus.Finished,
            "NOT_YET_RELEASED" => ReleaseStatus.Upcoming,
            "CANCELLED" => ReleaseStatus.Cancelled,
            "HIATUS" => ReleaseStatus.Hiatus,
            _ => (ReleaseStatus?)null,
        };

        var year = Int(Child(media, "startDate"), "year") ?? Int(media, "seasonYear");
        var endYear = status == ReleaseStatus.Airing ? null : Int(Child(media, "endDate"), "year");

        var score = Int(media, "averageScore");
        double? rating = score is > 0 ? Math.Round(score.Value / 10.0, 1) : null;

        DateTimeOffset? nextAiringAt = null;
        int? nextAiringEpisode = null;
        var next = Child(media, "nextAiringEpisode");
        if (Long(next, "airingAt") is { } airingAt)
        {
            nextAiringAt = DateTimeOffset.FromUnixTimeSeconds(airingAt);
            nextAiringEpisode = Int(next, "episode");
        }

        return new ProviderMetadata(
            Provider: MetadataProviders.AniList,
            ExternalId: id.ToString(CultureInfo.InvariantCulture),
            Title: display,
            OriginalTitle: original,
            Year: year,
            EndYear: endYear,
            Status: status,
            Overview: OverviewText.FromHtml(Str(media, "description")),
            ContentRating: Bool(media, "isAdult") ? "18+" : null,
            CommunityRating: rating,
            Genres: Strings(Child(media, "genres")),
            Tags: ParseTags(Child(media, "tags")),
            Studios: ParseStudios(Child(media, "studios")),
            Directors: [],
            Actors: [],
            RuntimeMinutes: Int(media, "duration"),
            Format: Str(media, "format"),
            Episodes: Int(media, "episodes"),
            PosterUrl: Str(Child(media, "coverImage"), "extraLarge") ?? Str(Child(media, "coverImage"), "large"),
            BackdropUrl: Str(media, "bannerImage"),
            NextAiringAt: nextAiringAt,
            NextAiringEpisode: nextAiringEpisode,
            Links: ParseLinks(media, id),
            ImdbId: null,
            AniListId: id,
            EpisodeTitles: ParseEpisodeTitles(Child(media, "streamingEpisodes")),
            TotalSeasons: null);
    }

    // ---------------------------------------------------------------- búsqueda

    public static IReadOnlyList<SearchCandidate> ParseSearch(string mediaArrayJson)
    {
        using var document = JsonDocument.Parse(mediaArrayJson);
        var candidates = new List<SearchCandidate>();
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return candidates;
        }

        foreach (var media in document.RootElement.EnumerateArray())
        {
            var id = Int(media, "id");
            if (id is null)
            {
                continue;
            }

            var title = Child(media, "title");
            var romaji = Str(title, "romaji");
            var english = Str(title, "english");
            var native = Str(title, "native");
            var display = english ?? romaji ?? native ?? $"AniList {id}";

            var titles = new[] { romaji, english, native }
                .Concat(Strings(Child(media, "synonyms")))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct()
                .ToList();

            var parsed = ParseMedia(media);
            candidates.Add(new SearchCandidate(
                ExternalId: id.Value.ToString(CultureInfo.InvariantCulture),
                Title: display,
                OriginalTitle: parsed.OriginalTitle,
                Titles: titles,
                Year: parsed.Year,
                EndYear: parsed.EndYear,
                Status: parsed.Status,
                Format: parsed.Format,
                PosterUrl: Str(Child(media, "coverImage"), "large") ?? parsed.PosterUrl,
                Url: $"https://anilist.co/anime/{id}",
                RawPayload: media.GetRawText()));
        }

        return candidates;
    }

    // ---------------------------------------------------------------- episodios

    /// <summary>
    /// Los títulos de <c>streamingEpisodes</c> que traen un número claro ("Episode 3 - …"), en orden de número. Los
    /// que no lo traen se ignoran: el episodio se queda como "Episodio N". Si un número se repite, gana el primero.
    /// </summary>
    public static IReadOnlyList<EpisodeTitle> ParseEpisodeTitles(JsonElement streamingEpisodes)
    {
        var titles = new Dictionary<int, string>();
        if (streamingEpisodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var episode in streamingEpisodes.EnumerateArray())
            {
                if (ParseEpisodeTitle(Str(episode, "title")) is { } parsed && parsed.Number > 0)
                {
                    titles.TryAdd(parsed.Number, parsed.Title);
                }
            }
        }

        return titles.OrderBy(pair => pair.Key).Select(pair => new EpisodeTitle(null, pair.Key, pair.Value)).ToList();
    }

    /// <summary>"Episode 3 - The Great Enemy" da (3, "The Great Enemy"). Sin número o sin título, null.</summary>
    public static (int Number, string Title)? ParseEpisodeTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = EpisodeTitlePattern.Match(raw);
        return match.Success
            ? (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups[2].Value)
            : null;
    }

    // ---------------------------------------------------------------- partes del detalle

    private static List<string> ParseTags(JsonElement tags)
    {
        if (tags.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return tags.EnumerateArray()
            .Where(tag => !Bool(tag, "isMediaSpoiler") && !Bool(tag, "isGeneralSpoiler"))
            .Select(tag => (Name: Str(tag, "name"), Rank: Int(tag, "rank") ?? 0))
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
            .OrderByDescending(tag => tag.Rank)
            .Select(tag => tag.Name!)
            .Distinct()
            .Take(MaxTags)
            .ToList();
    }

    // El estudio de animación primero, después los demás con el principal antes.
    private static List<string> ParseStudios(JsonElement studios)
    {
        var edges = Child(studios, "edges");
        if (edges.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return edges.EnumerateArray()
            .Select((edge, index) =>
            {
                var node = Child(edge, "node");
                return (
                    Name: Str(node, "name"),
                    Animation: Bool(node, "isAnimationStudio"),
                    Main: Bool(edge, "isMain"),
                    Index: index);
            })
            .Where(studio => !string.IsNullOrWhiteSpace(studio.Name))
            .OrderByDescending(studio => studio.Animation)
            .ThenByDescending(studio => studio.Main)
            .ThenBy(studio => studio.Index)
            .Select(studio => studio.Name!)
            .Distinct()
            .Take(MaxStudios)
            .ToList();
    }

    private static List<ExternalLink> ParseLinks(JsonElement media, int id)
    {
        var links = new List<ExternalLink>
        {
            new("AniList", Str(media, "siteUrl") ?? $"https://anilist.co/anime/{id}"),
        };

        if (Int(media, "idMal") is { } malId)
        {
            links.Add(new ExternalLink("MyAnimeList", $"https://myanimelist.net/anime/{malId}"));
        }

        var external = Child(media, "externalLinks");
        if (external.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in external.EnumerateArray())
            {
                if (Str(link, "site") == "Official Site" && Str(link, "url") is { } url &&
                    Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    links.Add(new ExternalLink("Sitio oficial", url));
                    break;
                }
            }
        }

        return links;
    }

    // ---------------------------------------------------------------- JSON

    private static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : default;

    private static string? Str(JsonElement element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : null;

    private static long? Long(JsonElement element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : null;

    private static bool Bool(JsonElement element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.True };

    private static List<string> Strings(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                .Select(item => item.GetString()!)
                .ToList()
            : [];
}
