using Mediateca.Core.Libraries;

namespace Mediateca.Core.Media;

/// <summary>Extensiones que reconoce el escáner (PLAN.md, sección 7).</summary>
public static class MediaFiles
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".webm", ".mkv" };

    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wav" };

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    public static bool IsAudio(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    /// <summary>Indica si el archivo va en ese tipo de biblioteca: música admite audio y las demás, video.</summary>
    public static bool BelongsTo(LibraryType type, string path) =>
        type == LibraryType.Music ? IsAudio(path) : IsVideo(path);
}
