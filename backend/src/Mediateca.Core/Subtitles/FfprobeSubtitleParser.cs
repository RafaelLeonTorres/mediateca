using System.Text.Json;

namespace Mediateca.Core.Subtitles;

/// <summary>
/// Lee la salida de <c>ffprobe -v error -print_format json -show_streams -select_streams s</c> y deja las pistas
/// de subtítulos de texto. Los subtítulos de imagen (PGS, VobSub, DVB) no se pueden mostrar como texto y se
/// descartan. Una salida vacía o dañada da una lista vacía: nunca lanza.
/// </summary>
public static class FfprobeSubtitleParser
{
    /// <summary>Códecs de subtítulos de texto que se pueden pasar a WebVTT.</summary>
    private static readonly HashSet<string> TextCodecs =
        new(StringComparer.OrdinalIgnoreCase) { "subrip", "ass", "ssa", "mov_text", "webvtt", "text" };

    public static bool IsTextCodec(string? codec) => codec is not null && TextCodecs.Contains(codec);

    public static IReadOnlyList<SubtitleTrack> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("streams", out var streams) ||
                streams.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var tracks = new List<SubtitleTrack>();
            foreach (var stream in streams.EnumerateArray())
            {
                if (stream.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var codec = GetString(stream, "codec_name");
                var type = GetString(stream, "codec_type");
                if (!IsTextCodec(codec) || (type is not null && !string.Equals(type, "subtitle", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!stream.TryGetProperty("index", out var indexElement) || !indexElement.TryGetInt32(out var index) || index < 0)
                {
                    continue;
                }

                var tags = stream.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Object
                    ? tagsElement
                    : (JsonElement?)null;
                var title = tags is { } t ? GetString(t, "title") : null;
                var disposition = stream.TryGetProperty("disposition", out var dispositionElement) &&
                                  dispositionElement.ValueKind == JsonValueKind.Object
                    ? dispositionElement
                    : (JsonElement?)null;

                var forced = (disposition is { } d1 && GetFlag(d1, "forced")) || LooksForced(title);
                var isDefault = disposition is { } d2 && GetFlag(d2, "default");

                tracks.Add(new SubtitleTrack(
                    $"e{index}",
                    string.Empty,
                    SubtitleLanguages.Normalize(tags is { } t2 ? GetString(t2, "language") : null),
                    codec!.ToLowerInvariant(),
                    SubtitleOrigin.Embedded,
                    isDefault,
                    forced,
                    string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                    index,
                    null));
            }

            return tracks;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>"Forced", "Forzados"... en el título de la pista.</summary>
    internal static bool LooksForced(string? title) =>
        title is not null &&
        (title.Contains("forced", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("forzad", StringComparison.OrdinalIgnoreCase));

    /// <summary>Lee una propiedad de texto sin distinguir mayúsculas (los MKV a veces traen <c>LANGUAGE</c>).</summary>
    private static string? GetString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool GetFlag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        ((value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number == 1) ||
         value.ValueKind == JsonValueKind.True);
}
