using Mediateca.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace Mediateca.Infrastructure.Scanning;

/// <summary>
/// Lee etiquetas, duración y carátula con TagLibSharp. Un archivo vacío, dañado o de un formato que
/// TagLib no reconoce no es un error del escaneo: se registra y se devuelve null.
/// </summary>
public sealed class TagLibAudioTagReader(ILogger<TagLibAudioTagReader> logger) : IAudioTagReader
{
    public AudioTags? ReadTags(string path)
    {
        try
        {
            // TagLib.File se escribe completo: "File" solo chocaría con System.IO.File.
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            var duration = file.Properties?.Duration.TotalSeconds;

            return new AudioTags(
                Title: NullIfBlank(tag?.Title),
                Artists: Clean(tag?.Performers),
                AlbumArtist: Clean(tag?.AlbumArtists).FirstOrDefault(),
                Album: NullIfBlank(tag?.Album),
                Year: tag is { Year: > 0 } ? (int)tag.Year : null,
                TrackNumber: tag is { Track: > 0 } ? (int)tag.Track : null,
                DiscNumber: tag is { Disc: > 0 } ? (int)tag.Disc : null,
                Genres: Clean(tag?.Genres),
                DurationSeconds: duration is > 0 ? duration : null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogDebug(ex, "No se pudieron leer las etiquetas de {Path}.", path);
            return null;
        }
    }

    public double? ReadDuration(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var duration = file.Properties?.Duration.TotalSeconds;
            return duration is > 0 ? duration : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogDebug(ex, "No se pudo leer la duración de {Path}.", path);
            return null;
        }
    }

    public EmbeddedCover? ReadCover(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var picture = file.Tag?.Pictures?.FirstOrDefault(p => p.Data is { Count: > 0 });
            return picture is null
                ? null
                : new EmbeddedCover(picture.Data.Data, picture.MimeType ?? "image/jpeg");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogDebug(ex, "No se pudo leer la carátula de {Path}.", path);
            return null;
        }
    }

    private static string? NullIfBlank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static IReadOnlyList<string> Clean(string[]? values) =>
        (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
