namespace Mediateca.Core.Media;

/// <summary>Álbum: agrupa las pistas de una carpeta de música.</summary>
public class MusicAlbum : MediaItem
{
    public string? AlbumArtist { get; set; }
}

/// <summary>Pista de un álbum: un archivo de audio.</summary>
public class Track : PlayableItem
{
    public int? TrackNumber { get; set; }

    public int? DiscNumber { get; set; }

    public string? Artist { get; set; }
}
