namespace Mediateca.Core.Subtitles;

/// <summary>Lo que dice el nombre de un subtítulo suelto: idioma, si es de forzados y el resto del nombre.</summary>
public sealed record ExternalSubtitleName(string? Language, bool IsForced, bool IsDefault, string? Title);

/// <summary>
/// Reconoce los subtítulos sueltos junto a un video. Un archivo cuenta si su nombre empieza con el del video y
/// termina en <c>.srt</c>, <c>.vtt</c>, <c>.ass</c> o <c>.ssa</c>; los segmentos de en medio dicen el idioma y si son
/// forzados: <c>01.srt</c>, <c>01.es.srt</c>, <c>01.spa.forced.srt</c>, <c>01.Spanish.ass</c>. Lo que no se reconoce
/// como idioma ni como marca queda como título ("01.Comentarios.srt").
/// </summary>
public static class ExternalSubtitleNames
{
    public static readonly string[] Extensions = [".srt", ".vtt", ".ass", ".ssa"];

    private static readonly HashSet<string> ForcedWords =
        new(StringComparer.OrdinalIgnoreCase) { "forced", "forzado", "forzados", "forzada", "forzadas" };

    public static bool IsSubtitleFile(string fileName) =>
        Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    /// <summary>Códec con el que se anuncia un archivo suelto, según su extensión.</summary>
    public static string CodecOf(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".srt" => "subrip",
            ".vtt" => "webvtt",
            ".ass" => "ass",
            ".ssa" => "ssa",
            _ => "text",
        };

    /// <summary>
    /// Interpreta el nombre de un archivo (solo el nombre, sin carpeta) respecto al del video. Null si no es un
    /// subtítulo de ese video.
    /// </summary>
    public static ExternalSubtitleName? Parse(string videoFileName, string candidateFileName)
    {
        if (!IsSubtitleFile(candidateFileName))
        {
            return null;
        }

        var videoName = Path.GetFileNameWithoutExtension(videoFileName);
        var stem = Path.GetFileNameWithoutExtension(candidateFileName);
        if (videoName.Length == 0 || !stem.StartsWith(videoName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = stem[videoName.Length..];
        if (rest.Length == 0)
        {
            return new ExternalSubtitleName(null, false, false, null);
        }

        // "010.srt" no es de "01.mp4": lo que sigue al nombre del video tiene que empezar con punto.
        if (rest[0] != '.')
        {
            return null;
        }

        string? language = null;
        var forced = false;
        var isDefault = false;
        var leftovers = new List<string>();
        foreach (var segment in rest.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ForcedWords.Contains(segment))
            {
                forced = true;
            }
            else if (string.Equals(segment, "default", StringComparison.OrdinalIgnoreCase))
            {
                isDefault = true;
            }
            else if (language is null && SubtitleLanguages.TryGetKnown(segment) is { } known)
            {
                language = known;
            }
            else
            {
                leftovers.Add(segment);
            }
        }

        return new ExternalSubtitleName(language, forced, isDefault, leftovers.Count == 0 ? null : string.Join(' ', leftovers));
    }
}
