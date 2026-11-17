namespace Mediateca.Core.Media;

/// <summary>Película: un archivo de video.</summary>
public class Movie : PlayableItem;

/// <summary>Serie o anime: agrupa temporadas o, en anime, episodios directamente.</summary>
public class Series : MediaItem;

/// <summary>Temporada de una serie.</summary>
public class Season : MediaItem
{
    public int? SeasonNumber { get; set; }
}

/// <summary>Episodio de una serie o de un anime.</summary>
public class Episode : PlayableItem
{
    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    /// <summary>Número absoluto, común en anime ("Mi Anime - 05").</summary>
    public int? AbsoluteNumber { get; set; }
}
