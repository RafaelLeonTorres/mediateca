namespace Mediateca.Core.Metadata;

/// <summary>
/// Respuesta guardada de un proveedor de metadatos, para no volver a pedirla: OMDb tiene un límite
/// diario y AniList, uno por minuto. Guarda el JSON tal como lo entregó el proveedor.
/// </summary>
public class MetadataCacheEntry
{
    /// <summary>
    /// "anilist" o "omdb" para el detalle de un elemento; "anilist-search" y "omdb-search" para las búsquedas;
    /// "omdb-season" para los episodios de una temporada.
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// ID de IMDb (tt0118799) o de AniList (21); en las búsquedas, un resumen (hash) de la consulta; en las
    /// temporadas, "tt0118799:2".
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>Datos del proveedor en JSON. Se guarda como jsonb.</summary>
    public required string Payload { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
