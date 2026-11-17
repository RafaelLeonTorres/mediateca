namespace Mediateca.Core.Media;

/// <summary>Elemento con un archivo que se puede reproducir: película, episodio o pista.</summary>
public abstract class PlayableItem : MediaItem
{
    /// <summary>Ruta completa del archivo. Sale del escaneo; el cliente nunca la manda.</summary>
    public required string FilePath { get; set; }

    public long FileSize { get; set; }

    public DateTimeOffset FileModifiedAt { get; set; }

    public double? DurationSeconds { get; set; }
}
