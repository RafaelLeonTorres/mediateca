using Mediateca.Core.Libraries;

namespace Mediateca.Core.Media;

/// <summary>
/// Base de todo lo que aparece en una biblioteca: películas, series, temporadas, episodios,
/// álbumes y pistas. Todos se guardan en una sola tabla (herencia TPH), así una consulta sirve
/// para la búsqueda, "Agregado recientemente" y "Continuar viendo".
/// </summary>
public abstract class MediaItem
{
    public Guid Id { get; set; }

    public Guid LibraryId { get; set; }

    public Library? Library { get; set; }

    /// <summary>Serie de una temporada, temporada de un episodio o álbum de una pista.</summary>
    public Guid? ParentId { get; set; }

    public MediaItem? Parent { get; set; }

    public List<MediaItem> Children { get; set; } = [];

    public required string Title { get; set; }

    /// <summary>Título para ordenar, sin artículos iniciales ("Matrix" en lugar de "The Matrix").</summary>
    public required string SortTitle { get; set; }

    public int? Year { get; set; }

    public string? Overview { get; set; }

    /// <summary>Clasificación por edades: PG-13, TV-MA, o 18+ en el anime para adultos.</summary>
    public string? ContentRating { get; set; }

    /// <summary>Calificación de la comunidad (IMDb o AniDB), de 0 a 10.</summary>
    public double? CommunityRating { get; set; }

    public List<string> Genres { get; set; } = [];

    public string? ImdbId { get; set; }

    public int? AniDbId { get; set; }

    /// <summary>
    /// Ruta completa de la imagen: una imagen junto a los medios (poster.jpg, folder.jpg…) o un archivo de
    /// la carpeta de datos de la app (carátula extraída, o póster descargado). Nunca sale al cliente.
    /// </summary>
    public string? PosterPath { get; set; }

    public DateTimeOffset DateAdded { get; set; }

    public MetadataStatus MetadataStatus { get; set; }
}
