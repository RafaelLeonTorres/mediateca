using Mediateca.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// Descarga el póster o el fondo de un proveedor a <c>{DataPath}/images/</c> (una sola vez: después se sirve de
/// ahí). La dirección viene de la respuesta de un proveedor, así que solo se acepta HTTPS en los servidores de
/// imágenes conocidos (los de AniList e IMDb), sin seguir redirecciones, con límite de tamaño y comprobando que
/// lo descargado sea de verdad una imagen (JPEG, PNG o WebP) por sus primeros bytes.
/// </summary>
public sealed class ImageDownloader(
    IHttpClientFactory http,
    IOptions<StorageOptions> storage,
    ILogger<ImageDownloader> logger)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    // Servidores de imágenes de los proveedores: s4.anilist.co (AniList) y m.media-amazon.com (los pósteres de IMDb en OMDb).
    private static readonly string[] AllowedHostSuffixes = ["anilist.co", "media-amazon.com"];

    /// <summary>Si la dirección es HTTPS y de un servidor de imágenes permitido.</summary>
    public static bool IsAllowed(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        AllowedHostSuffixes.Any(suffix =>
            uri.Host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Descarga la imagen a <c>{ImagesPath}/{id}-{kind}.{ext}</c> y devuelve la ruta, o null si no se pudo (no es un
    /// error del elemento: se queda sin imagen y se vuelve a intentar en la siguiente actualización).
    /// </summary>
    public async Task<string?> DownloadAsync(string? url, Guid itemId, string kind, CancellationToken cancellationToken)
    {
        if (!IsAllowed(url))
        {
            if (url is not null)
            {
                logger.LogWarning("No se descarga la imagen {Kind} de {Item}: la dirección no es de un servidor permitido.", kind, itemId);
            }

            return null;
        }

        try
        {
            using var client = http.CreateClient(HttpClientNames.Images);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("La imagen {Kind} de {Item} respondió {Status}.", kind, itemId, (int)response.StatusCode);
                return null;
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                logger.LogWarning("La imagen {Kind} de {Item} es demasiado grande.", kind, itemId);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bytes = await ReadLimitedAsync(stream, cancellationToken);
            if (bytes is null)
            {
                logger.LogWarning("La imagen {Kind} de {Item} es demasiado grande.", kind, itemId);
                return null;
            }

            var extension = Sniff(bytes);
            if (extension is null)
            {
                logger.LogWarning("La imagen {Kind} de {Item} no es un JPEG, PNG ni WebP.", kind, itemId);
                return null;
            }

            var folder = storage.Value.ImagesPath;
            Directory.CreateDirectory(folder);
            var prefix = $"{itemId:N}-{kind}";

            // Una imagen anterior (con otra extensión) ya no sirve.
            foreach (var old in Directory.EnumerateFiles(folder, prefix + ".*"))
            {
                File.Delete(old);
            }

            var path = Path.Combine(folder, prefix + extension);
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "No se pudo descargar la imagen {Kind} de {Item}.", kind, itemId);
            return null;
        }
    }

    /// <summary>La extensión (con punto) según los primeros bytes, o null si no es una imagen admitida.</summary>
    public static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return ".webp";
        }

        return null;
    }

    private static async Task<byte[]?> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
