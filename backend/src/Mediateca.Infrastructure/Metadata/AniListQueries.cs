namespace Mediateca.Infrastructure.Metadata;

/// <summary>Las consultas GraphQL de AniList (PLAN.md, sección 8). La búsqueda pide los mismos campos que el detalle.</summary>
public static class AniListQueries
{
    // description llega con HTML (se limpia en AniListParser). Las etiquetas traen sus marcas de spoiler para filtrarlas.
    private const string MediaFields = """
        id
        idMal
        siteUrl
        title { romaji english native }
        synonyms
        description
        startDate { year month day }
        endDate { year month day }
        status
        season
        seasonYear
        format
        episodes
        duration
        genres
        tags { name rank isMediaSpoiler isGeneralSpoiler }
        studios { edges { isMain node { name isAnimationStudio } } }
        averageScore
        isAdult
        coverImage { extraLarge large }
        bannerImage
        nextAiringEpisode { airingAt episode }
        externalLinks { site url }
        streamingEpisodes { title thumbnail }
        """;

    /// <summary>Hasta 10 resultados por título, con todos los datos de cada uno.</summary>
    public const string Search =
        "query ($search: String) { Page(perPage: 10) { media(search: $search, type: ANIME) { " + MediaFields + " } } }";

    public const string Details =
        "query ($id: Int) { Media(id: $id, type: ANIME) { " + MediaFields + " } }";
}
