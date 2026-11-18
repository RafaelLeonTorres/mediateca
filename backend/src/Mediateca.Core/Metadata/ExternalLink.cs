using System.Text.Json;

namespace Mediateca.Core.Metadata;

/// <summary>Enlace de la ficha a otro sitio: AniList, MyAnimeList, IMDb…</summary>
public sealed record ExternalLink(string Site, string Url);

/// <summary>
/// Los enlaces externos se guardan en una sola columna jsonb (<c>MediaItem.ExternalLinksJson</c>): son una lista
/// corta que siempre se lee y se escribe completa, así que no vale la pena una tabla.
/// </summary>
public static class ExternalLinks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>JSON para guardar, o null si no hay enlaces.</summary>
    public static string? Serialize(IReadOnlyCollection<ExternalLink> links) =>
        links.Count == 0 ? null : JsonSerializer.Serialize(links, Options);

    /// <summary>Lee el JSON guardado; si está vacío o dañado, una lista vacía (nunca lanza).</summary>
    public static IReadOnlyList<ExternalLink> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var links = JsonSerializer.Deserialize<List<ExternalLink>>(json, Options) ?? [];
            return links.Where(link => !string.IsNullOrWhiteSpace(link.Site) && !string.IsNullOrWhiteSpace(link.Url)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
