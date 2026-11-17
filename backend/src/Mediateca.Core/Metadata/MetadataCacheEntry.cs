namespace Mediateca.Core.Metadata;

/// <summary>
/// Respuesta guardada de un proveedor de metadatos, para no volver a pedirla: OMDb tiene un límite
/// diario y AniDB banea a quien pide el mismo anime más de una vez al día.
/// </summary>
public class MetadataCacheEntry
{
    /// <summary>"omdb" o "anidb".</summary>
    public required string Provider { get; set; }

    /// <summary>ID de IMDb (tt0118799) o de AniDB.</summary>
    public required string ExternalId { get; set; }

    /// <summary>Datos del proveedor en JSON. Se guarda como jsonb.</summary>
    public required string Payload { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
