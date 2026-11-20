using Mediateca.Core.Libraries;

namespace Mediateca.Core.Media;

/// <summary>Extensiones que reconoce el escáner (PLAN.md, sección 7).</summary>
public static class MediaFiles
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".webm", ".mkv" };

    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wav" };

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    public static bool IsAudio(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Tipo MIME con el que se sirve el archivo (PLAN.md, sección 9), según su extensión. Null si la
    /// extensión no es una de las que reconoce el escáner.
    /// </summary>
    public static string? ContentTypeOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            // QuickTime (.mov) tiene la misma estructura que MP4. Los navegadores aceptan video/mp4 y no siempre
            // video/quicktime, así que se sirve como MP4; si sus códecs no son compatibles (ProRes, por ejemplo),
            // el reproductor ofrece descargarlo.
            ".mov" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            ".ogg" or ".opus" => "audio/ogg",
            ".wav" => "audio/wav",
            _ => null,
        };

    /// <summary>Indica si el archivo va en ese tipo de biblioteca: música admite audio y las demás, video.</summary>
    public static bool BelongsTo(LibraryType type, string path) =>
        type == LibraryType.Music ? IsAudio(path) : IsVideo(path);
}
