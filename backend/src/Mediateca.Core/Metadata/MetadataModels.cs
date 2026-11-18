using Mediateca.Core.Media;

namespace Mediateca.Core.Metadata;

/// <summary>Qué se busca en un proveedor que distingue películas de series (OMDb).</summary>
public enum MetadataSearchKind
{
    Any,
    Movie,
    Series,
}

/// <summary>Búsqueda en un proveedor: el texto, el año (si se sabe) y si es película o serie.</summary>
public sealed record SearchQuery(string Text, int? Year, MetadataSearchKind Kind);

/// <summary>
/// Un resultado de búsqueda. <c>Titles</c> son todos los nombres con los que se compara (romaji, inglés,
/// original y sinónimos); <c>Format</c> es el formato del proveedor (<c>TV</c>, <c>MOVIE</c>, <c>OVA</c>…;
/// <c>movie</c> o <c>series</c> en OMDb). <c>RawPayload</c> es la respuesta completa del proveedor cuando la
/// búsqueda ya trae todos los datos (AniList): así elegir un resultado no cuesta otra solicitud.
/// </summary>
public sealed record SearchCandidate(
    string ExternalId,
    string Title,
    string? OriginalTitle,
    IReadOnlyList<string> Titles,
    int? Year,
    int? EndYear,
    ReleaseStatus? Status,
    string? Format,
    string? PosterUrl,
    string? Url,
    string? RawPayload = null);

/// <summary>Título de un episodio. <c>Season</c> es nulo cuando el proveedor numera de corrido (AniList).</summary>
public sealed record EpisodeTitle(int? Season, int Number, string Title);

/// <summary>Lo que un proveedor sabe de una serie o película, ya traducido a los campos de Mediateca.</summary>
public sealed record ProviderMetadata(
    string Provider,
    string ExternalId,
    string Title,
    string? OriginalTitle,
    int? Year,
    int? EndYear,
    ReleaseStatus? Status,
    string? Overview,
    string? ContentRating,
    double? CommunityRating,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Studios,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Actors,
    int? RuntimeMinutes,
    string? Format,
    int? Episodes,
    string? PosterUrl,
    string? BackdropUrl,
    DateTimeOffset? NextAiringAt,
    int? NextAiringEpisode,
    IReadOnlyList<ExternalLink> Links,
    string? ImdbId,
    int? AniListId,
    IReadOnlyList<EpisodeTitle> EpisodeTitles,
    int? TotalSeasons);
