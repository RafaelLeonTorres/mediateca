namespace Mediateca.Core.Scanning;

/// <summary>Etiquetas de un archivo de audio. Lo que el archivo no trae queda en null o vacío.</summary>
public sealed record AudioTags(
    string? Title,
    IReadOnlyList<string> Artists,
    string? AlbumArtist,
    string? Album,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    IReadOnlyList<string> Genres,
    double? DurationSeconds);

/// <summary>Carátula incrustada en un archivo de audio.</summary>
public sealed record EmbeddedCover(byte[] Data, string MimeType);

/// <summary>
/// Lee las etiquetas de los archivos. Ninguno de sus métodos lanza excepciones por un archivo
/// dañado o de un formato que no reconoce: devuelven null y el escáner usa el nombre de la ruta.
/// </summary>
public interface IAudioTagReader
{
    /// <summary>Etiquetas de un archivo de audio, o null si no se pueden leer.</summary>
    AudioTags? ReadTags(string path);

    /// <summary>Duración en segundos de un archivo de audio o de video (mp4, mkv…), o null.</summary>
    double? ReadDuration(string path);

    /// <summary>Primera imagen incrustada en el archivo de audio, o null.</summary>
    EmbeddedCover? ReadCover(string path);
}
