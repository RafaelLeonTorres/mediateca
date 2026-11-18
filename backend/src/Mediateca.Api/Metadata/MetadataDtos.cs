namespace Mediateca.Api.Metadata;

/// <summary>
/// Metadatos de una biblioteca para la administración: cuántas series o películas hay en cada estado, de qué proveedor
/// salen y si está disponible. <c>Message</c> explica por qué no (falta la clave de OMDb, está en pausa).
/// <c>PausedUntil</c> es una fecha UTC: la cola sigue sola después.
/// </summary>
public sealed record MetadataSummary(
    string Provider,
    string ProviderName,
    int Pending,
    int Matched,
    int NotFound,
    int Error,
    bool Configured,
    DateTimeOffset? PausedUntil,
    string? Message);

/// <summary>Respuesta de POST /api/admin/libraries/{id}/metadata/fetch: cuántos elementos esperan en la cola.</summary>
public sealed record MetadataQueueResponse(int Queued, bool Configured, string? Message);

/// <summary>Un resultado de la búsqueda del diálogo Identificar. <c>ExternalId</c> es el id de AniList o el de IMDb.</summary>
public sealed record MetadataCandidate(
    string ExternalId,
    string Title,
    string? OriginalTitle,
    int? Year,
    int? EndYear,
    string? Format,
    string? Status,
    string? PosterUrl,
    string? Url);

/// <summary>Respuesta de GET /api/admin/items/{id}/metadata/search.</summary>
public sealed record MetadataSearchResponse(string Provider, string ProviderName, IReadOnlyList<MetadataCandidate> Results);

/// <summary>Cuerpo de POST /api/admin/items/{id}/metadata/identify: un id o una dirección de AniList o de IMDb.</summary>
public sealed record IdentifyRequest(string? Identifier);

/// <summary>Resultado de Identificar o Actualizar. <c>Status</c>: Matched o NotFound.</summary>
public sealed record MetadataResultResponse(string Status, string? Message, string? Title);
