namespace Mediateca.Core.Media;

/// <summary>Estado de la búsqueda de metadatos (OMDb o AniDB) de un elemento.</summary>
public enum MetadataStatus
{
    Pending,
    Matched,
    NotFound,
    Error,
}
