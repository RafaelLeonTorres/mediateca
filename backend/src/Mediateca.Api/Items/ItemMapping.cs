using Mediateca.Core.Libraries;
using Mediateca.Core.Media;

namespace Mediateca.Api.Items;

/// <summary>Convierte las entidades en los DTO de la API.</summary>
internal static class ItemMapping
{
    /// <summary>Nombre del tipo, igual al discriminador de la tabla.</summary>
    public static string KindOf(MediaItem item) => item switch
    {
        Movie => "Movie",
        Series => "Series",
        Season => "Season",
        Episode => "Episode",
        MusicAlbum => "MusicAlbum",
        Track => "Track",
        _ => "Item",
    };

    public static ItemSummary ToSummary(MediaItem item) =>
        new(item.Id, KindOf(item), item.Title, item.Year, ArtistOf(item), HasImage(item));

    public static ChildItem ToChild(MediaItem item) =>
        new(
            item.Id,
            KindOf(item),
            item.Title,
            item.Year,
            item.Overview,
            HasImage(item),
            SeasonOf(item),
            EpisodeOf(item),
            AbsoluteOf(item),
            ArtistOf(item),
            TrackOf(item),
            DiscOf(item),
            DurationOf(item));

    public static ItemDetail ToDetail(
        MediaItem item, string libraryName, LibraryType libraryType, IReadOnlyList<ItemReference> ancestors) =>
        new(
            item.Id,
            KindOf(item),
            item.LibraryId,
            libraryName,
            libraryType,
            item.ParentId,
            ancestors.Count > 0 ? ancestors[^1].Title : null,
            ancestors,
            item.Title,
            item.Year,
            item.Overview,
            item.ContentRating,
            item.CommunityRating,
            item.Genres,
            HasImage(item),
            item.DateAdded,
            SeasonOf(item),
            EpisodeOf(item),
            AbsoluteOf(item),
            ArtistOf(item),
            TrackOf(item),
            DiscOf(item),
            DurationOf(item),
            (item as PlayableItem)?.FileSize);

    // El cliente nunca ve PosterPath, solo si hay imagen; la imagen sale de /api/items/{id}/image.
    public static bool HasImage(MediaItem item) => !string.IsNullOrEmpty(item.PosterPath);

    public static string? ArtistOf(MediaItem item) => item switch
    {
        MusicAlbum album => album.AlbumArtist,
        Track track => track.Artist,
        _ => null,
    };

    public static int? SeasonOf(MediaItem item) => item switch
    {
        Season season => season.SeasonNumber,
        Episode episode => episode.SeasonNumber,
        _ => null,
    };

    public static int? EpisodeOf(MediaItem item) => item is Episode episode ? episode.EpisodeNumber : null;

    public static int? AbsoluteOf(MediaItem item) => item is Episode episode ? episode.AbsoluteNumber : null;

    public static int? TrackOf(MediaItem item) => item is Track track ? track.TrackNumber : null;

    public static int? DiscOf(MediaItem item) => item is Track track ? track.DiscNumber : null;

    public static double? DurationOf(MediaItem item) => (item as PlayableItem)?.DurationSeconds;

    /// <summary>
    /// Orden de los hijos: temporadas por número; episodios por temporada, episodio y número
    /// absoluto; pistas por disco y pista. Las temporadas van antes que los episodios sueltos.
    /// </summary>
    public static List<MediaItem> OrderChildren(IEnumerable<MediaItem> children) =>
        children
            .OrderBy(item => item switch { Season => 0, Episode => 1, Track => 2, _ => 3 })
            .ThenBy(item => SeasonOf(item) ?? int.MaxValue)
            .ThenBy(item => EpisodeOf(item) ?? int.MaxValue)
            .ThenBy(item => AbsoluteOf(item) ?? int.MaxValue)
            .ThenBy(item => DiscOf(item) ?? 1)
            .ThenBy(item => TrackOf(item) ?? int.MaxValue)
            .ThenBy(item => item.SortTitle, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .ToList();
}
