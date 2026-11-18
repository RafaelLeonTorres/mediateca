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

    /// <summary>Calificación de la comunidad (IMDb o AniList), de 0 a 10.</summary>
    public double? CommunityRating { get; set; }

    public List<string> Genres { get; set; } = [];

    public string? ImdbId { get; set; }

    /// <summary>Id del anime en AniList (la etiqueta <c>[anilistid-21]</c> de la carpeta o el que eligió Identificar).</summary>
    public int? AniListId { get; set; }

    /// <summary>
    /// Ruta completa de la imagen: una imagen junto a los medios (poster.jpg, folder.jpg…) o un archivo de
    /// la carpeta de datos de la app (carátula extraída, o póster descargado). Nunca sale al cliente.
    /// </summary>
    public string? PosterPath { get; set; }

    public DateTimeOffset DateAdded { get; set; }

    public MetadataStatus MetadataStatus { get; set; }

    // ---- Metadatos de un proveedor (fase 4). Solo las series y las películas de primer nivel los tienen;
    // las temporadas y los episodios heredan lo que se muestra de su serie.

    /// <summary>Título original o romaji ("Aishiteru Game wo Owarasetai"); el título en inglés va en <see cref="Title"/>.</summary>
    public string? OriginalTitle { get; set; }

    /// <summary>Año en que termina la emisión. Nulo mientras sigue en emisión o si es una película. El de inicio es <see cref="Year"/>.</summary>
    public int? EndYear { get; set; }

    /// <summary>Si está en emisión, terminó o todavía no sale. Nulo si el proveedor no lo dice.</summary>
    public ReleaseStatus? Status { get; set; }

    public List<string> Studios { get; set; } = [];

    /// <summary>Etiquetas del proveedor (Female Protagonist, School…), sin las marcadas como spoiler, de la más relevante a la menos.</summary>
    public List<string> Tags { get; set; } = [];

    public List<string> Directors { get; set; } = [];

    public List<string> Actors { get; set; } = [];

    /// <summary>Duración típica en minutos (de un episodio o de la película) según el proveedor.</summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>
    /// Imagen de fondo: un <c>backdrop.jpg</c> junto a los medios o, si no hay, el que descargó el proveedor
    /// a la carpeta de datos. Igual que <see cref="PosterPath"/>, nunca sale al cliente.
    /// </summary>
    public string? BackdropPath { get; set; }

    /// <summary>Enlaces externos (AniList, MyAnimeList, IMDb…) como JSON (<c>[{"site":"AniList","url":"…"}]</c>); se guarda como jsonb.</summary>
    public string? ExternalLinksJson { get; set; }

    /// <summary>Cuándo sale el siguiente episodio (UTC) y cuál es. Solo para lo que está en emisión.</summary>
    public DateTimeOffset? NextAiringAt { get; set; }

    public int? NextAiringEpisode { get; set; }

    /// <summary>Cuándo se aplicaron por última vez los metadatos del proveedor.</summary>
    public DateTimeOffset? MetadataUpdatedAt { get; set; }

    /// <summary>Por qué quedó en <see cref="MetadataStatus.NotFound"/> o <see cref="MetadataStatus.Error"/> (o qué falta para seguir).</summary>
    public string? MetadataMessage { get; set; }

    /// <summary>
    /// Clave con la que el escáner reconoce a una serie ya guardada (título y año que salen de su carpeta).
    /// Los metadatos cambian <see cref="Title"/> y <see cref="Year"/>; sin esta clave, el siguiente escaneo no la
    /// reconocería y crearía otra. Nulo en las series anteriores a los metadatos y en lo que no es una serie.
    /// </summary>
    public string? ScanKey { get; set; }
}
