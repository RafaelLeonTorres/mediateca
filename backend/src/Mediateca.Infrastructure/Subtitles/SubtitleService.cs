using System.Text;
using System.Text.RegularExpressions;
using Mediateca.Core.Media;
using Mediateca.Core.Subtitles;
using Mediateca.Infrastructure.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Subtitles;

/// <summary>Cómo salió la petición de un subtítulo.</summary>
public enum SubtitleStatus
{
    Ok,

    /// <summary>La pista no existe (o el archivo ya no está, o está fuera de las carpetas de medios).</summary>
    NotFound,

    /// <summary>La pista existe pero no se pudo convertir (FFmpeg falló, no está instalado o se pasó del tiempo).</summary>
    Failed,
}

/// <summary>Un subtítulo ya en WebVTT, o por qué no se pudo.</summary>
public sealed record SubtitleVtt(SubtitleStatus Status, string Content = "");

/// <summary>Cómo salió la subida de un subtítulo.</summary>
public enum SubtitleUploadStatus
{
    Ok,

    /// <summary>El elemento no es un video (una pista de música, por ejemplo).</summary>
    NotVideo,

    /// <summary>No es un subtítulo de texto que se pueda leer: otra extensión, vacío o sin ningún cue.</summary>
    Invalid,

    /// <summary>Pesa más de <see cref="SubtitleService.MaxUploadBytes"/>.</summary>
    TooLarge,

    /// <summary>El video ya tiene <see cref="SubtitleService.MaxUploadsPerItem"/> subtítulos subidos.</summary>
    TooMany,

    /// <summary>No se pudo guardar en la carpeta de datos.</summary>
    Failed,
}

/// <summary>La pista que quedó al subir un subtítulo, o por qué no se pudo (con un mensaje para el usuario).</summary>
public sealed record SubtitleUpload(SubtitleUploadStatus Status, SubtitleTrack? Track = null, string? Error = null);

/// <summary>Cómo salió borrar un subtítulo subido.</summary>
public enum SubtitleDeleteStatus
{
    Deleted,

    /// <summary>No existe, o la pista no es un subtítulo subido (las del archivo no se borran desde aquí).</summary>
    NotFound,

    /// <summary>Lo subió otro usuario y quien lo pide no es administrador.</summary>
    Forbidden,

    /// <summary>No se pudo borrar de la carpeta de datos.</summary>
    Failed,
}

/// <summary>
/// Subtítulos de un video (PLAN.md, secciones 9 y 14): lista las pistas incrustadas (con ffprobe), los
/// archivos sueltos de la carpeta y los que subieron los usuarios, y entrega cada una en WebVTT. Las
/// incrustadas se extraen con ffmpeg y el resultado se guarda en <c>{DataPath}/subtitles</c>, así que cada pista
/// se extrae una sola vez. Lo que trae cada archivo se guarda en memoria por ruta y fecha de modificación; los
/// subidos (en <c>{DataPath}/uploads/subtitles</c>) se leen cada vez. Si ffprobe o ffmpeg no están instalados no
/// falla nada: solo faltan las pistas incrustadas, y se avisa una vez en el log.
/// </summary>
public sealed partial class SubtitleService
{
    /// <summary>Un subtítulo suelto más grande que esto no es un subtítulo: no se lee.</summary>
    private const long MaxExternalBytes = 8 * 1024 * 1024;

    /// <summary>Lo más que puede pesar un subtítulo subido (un ASS con fuentes incrustadas llega a unos MB).</summary>
    public const long MaxUploadBytes = 8 * 1024 * 1024;

    /// <summary>Subtítulos subidos por video, como máximo.</summary>
    public const int MaxUploadsPerItem = 20;

    /// <summary>Lo más largo que se conserva del nombre de un archivo subido para su etiqueta.</summary>
    private const int MaxTitleLength = 100;

    private readonly IProcessRunner _processes;
    private readonly IMemoryCache _cache;
    private readonly MediaToolsOptions _tools;
    private readonly StorageOptions _storage;
    private readonly ILogger<SubtitleService> _logger;
    private readonly SemaphoreSlim _extractions;
    private readonly UploadedSubtitleStore _uploads;

    /// <summary>Una subida o un borrado a la vez: así el límite por video se respeta.</summary>
    private readonly SemaphoreSlim _uploadLock = new(1, 1);
    private int _warnedFfprobe;
    private int _warnedFfmpeg;

    public SubtitleService(
        IProcessRunner processes,
        IMemoryCache cache,
        IOptions<MediaToolsOptions> tools,
        IOptions<StorageOptions> storage,
        ILogger<SubtitleService> logger)
    {
        _processes = processes;
        _cache = cache;
        _tools = tools.Value;
        _storage = storage.Value;
        _logger = logger;
        _extractions = new SemaphoreSlim(Math.Max(1, _tools.MaxConcurrentExtractions));
        _uploads = new UploadedSubtitleStore(Path.Combine(_storage.DataPath, "uploads", "subtitles"));
    }

    /// <summary>Carpeta donde se guardan los subtítulos ya extraídos.</summary>
    public string CacheFolder => Path.Combine(_storage.DataPath, "subtitles");

    /// <summary>Carpeta de los subtítulos subidos, una subcarpeta por video.</summary>
    public string UploadsFolder => _uploads.Root;

    /// <summary>
    /// Pistas de texto del video <paramref name="itemId"/>: primero las incrustadas, después los archivos sueltos y al
    /// final las subidas por los usuarios, ya con su etiqueta.
    /// </summary>
    public async Task<IReadOnlyList<SubtitleTrack>> ListAsync(Guid itemId, string videoPath, CancellationToken cancellationToken)
    {
        if (!MediaFiles.IsVideo(videoPath) || !File.Exists(videoPath))
        {
            return [];
        }

        var fromFile = await ListFromFileAsync(videoPath, cancellationToken);
        return SubtitleLabels.Assign([.. fromFile, .. _uploads.List(itemId)]);
    }

    /// <summary>Lo que trae el archivo (incrustadas y sueltas), todavía sin etiqueta; en memoria por ruta y fechas.</summary>
    private async Task<IReadOnlyList<SubtitleTrack>> ListFromFileAsync(string videoPath, CancellationToken cancellationToken)
    {
        // La carpeta entra en la clave: un archivo .srt que se agrega o se quita cambia su fecha.
        var key = $"subtitles|{videoPath}|{File.GetLastWriteTimeUtc(videoPath).Ticks}|" +
                  $"{Directory.GetLastWriteTimeUtc(Path.GetDirectoryName(videoPath) ?? ".").Ticks}";
        if (_cache.TryGetValue(key, out IReadOnlyList<SubtitleTrack>? cached) && cached is not null)
        {
            return cached;
        }

        var (embedded, conclusive) = await ProbeEmbeddedAsync(videoPath, cancellationToken);
        IReadOnlyList<SubtitleTrack> tracks = [.. embedded, .. DiscoverExternal(videoPath)];

        // Si ffprobe falló o se pasó del tiempo, el resultado no se confía por mucho: se vuelve a intentar pronto.
        _cache.Set(key, tracks, conclusive
            ? new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(12) }
            : new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        return tracks;
    }

    /// <summary>El subtítulo <paramref name="trackId"/> del video, en WebVTT.</summary>
    public async Task<SubtitleVtt> GetVttAsync(Guid itemId, string videoPath, string trackId, CancellationToken cancellationToken)
    {
        var track = (await ListAsync(itemId, videoPath, cancellationToken)).FirstOrDefault(t => t.Id == trackId);
        if (track is null)
        {
            return new SubtitleVtt(SubtitleStatus.NotFound);
        }

        return track.Origin switch
        {
            SubtitleOrigin.Embedded => await ExtractEmbeddedAsync(itemId, videoPath, track, cancellationToken),
            SubtitleOrigin.Uploaded => await ReadUploadedAsync(track, cancellationToken),
            _ => await ReadExternalAsync(itemId, track, cancellationToken),
        };
    }

    // ───────────────────────── incrustadas ─────────────────────────

    private async Task<(IReadOnlyList<SubtitleTrack> Tracks, bool Conclusive)> ProbeEmbeddedAsync(
        string videoPath, CancellationToken cancellationToken)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest(
                _tools.Ffprobe,
                ["-v", "error", "-print_format", "json", "-show_streams", "-select_streams", "s", videoPath],
                _tools.ProbeTimeout),
            cancellationToken);

        if (result.Outcome == ProcessOutcome.NotInstalled)
        {
            WarnOnce(ref _warnedFfprobe, "ffprobe", _tools.Ffprobe, "las pistas de subtítulos incrustadas en los videos");
            return ([], true);
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "ffprobe no pudo leer {Path} ({Outcome}, código {ExitCode}): {Error}",
                videoPath, result.Outcome, result.ExitCode, Truncate(result.StandardError));
            return ([], false);
        }

        return (FfprobeSubtitleParser.Parse(result.StandardOutput), true);
    }

    private async Task<SubtitleVtt> ExtractEmbeddedAsync(
        Guid itemId, string videoPath, SubtitleTrack track, CancellationToken cancellationToken)
    {
        if (track.StreamIndex is not { } streamIndex || !File.Exists(videoPath))
        {
            return new SubtitleVtt(SubtitleStatus.NotFound);
        }

        var cacheFile = CacheFileOf(itemId, track.Id, File.GetLastWriteTimeUtc(videoPath).Ticks);
        if (await TryReadCacheAsync(cacheFile, cancellationToken) is { } stored)
        {
            return new SubtitleVtt(SubtitleStatus.Ok, stored);
        }

        await _extractions.WaitAsync(cancellationToken);
        try
        {
            // Otra solicitud pudo terminar la misma extracción mientras esta esperaba su turno.
            if (await TryReadCacheAsync(cacheFile, cancellationToken) is { } justStored)
            {
                return new SubtitleVtt(SubtitleStatus.Ok, justStored);
            }

            var vtt = await ConvertWithFfmpegAsync(
                ["-v", "error", "-nostdin", "-i", videoPath, "-map", $"0:{streamIndex}", "-f", "webvtt", "pipe:1"],
                $"la pista {track.Id} de {videoPath}",
                cancellationToken);
            if (vtt is null)
            {
                return new SubtitleVtt(SubtitleStatus.Failed);
            }

            await StoreCacheAsync(cacheFile, itemId, track.Id, vtt, cancellationToken);
            return new SubtitleVtt(SubtitleStatus.Ok, vtt);
        }
        finally
        {
            _extractions.Release();
        }
    }

    // ───────────────────────── sueltas ─────────────────────────

    private IEnumerable<SubtitleTrack> DiscoverExternal(string videoPath)
    {
        var folder = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrEmpty(folder))
        {
            return [];
        }

        var videoName = Path.GetFileName(videoPath);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return files
            .Select(file => (File: file, Name: ExternalSubtitleNames.Parse(videoName, Path.GetFileName(file))))
            .Where(candidate => candidate.Name is not null && IsServable(candidate.File))
            .Select(candidate => (candidate.File, Name: candidate.Name!))
            .OrderBy(candidate => Path.GetFileName(candidate.File), StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => Path.GetFileName(candidate.File), StringComparer.Ordinal)
            .Select((candidate, index) => new SubtitleTrack(
                $"x{index}",
                string.Empty,
                candidate.Name.Language,
                ExternalSubtitleNames.CodecOf(candidate.File),
                SubtitleOrigin.External,
                candidate.Name.IsDefault,
                candidate.Name.IsForced,
                candidate.Name.Title,
                null,
                candidate.File))
            .ToList();
    }

    private async Task<SubtitleVtt> ReadExternalAsync(Guid itemId, SubtitleTrack track, CancellationToken cancellationToken)
    {
        var path = track.FilePath;
        if (string.IsNullOrEmpty(path) || !IsServable(path))
        {
            return new SubtitleVtt(SubtitleStatus.NotFound);
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".ass" or ".ssa")
        {
            return await ConvertAssAsync(itemId, track, path, cancellationToken);
        }

        var text = SubtitleConverter.Decode(await File.ReadAllBytesAsync(path, cancellationToken));
        return new SubtitleVtt(SubtitleStatus.Ok, extension == ".srt" ? SubtitleConverter.SrtToVtt(text) : text);
    }

    /// <summary>ASS y SSA: con ffmpeg si está instalado; si no, o si falla, con el convertidor básico de C#.</summary>
    private async Task<SubtitleVtt> ConvertAssAsync(Guid itemId, SubtitleTrack track, string path, CancellationToken cancellationToken)
    {
        var cacheFile = CacheFileOf(itemId, track.Id, File.GetLastWriteTimeUtc(path).Ticks);
        if (await TryReadCacheAsync(cacheFile, cancellationToken) is { } stored)
        {
            return new SubtitleVtt(SubtitleStatus.Ok, stored);
        }

        string? vtt;
        await _extractions.WaitAsync(cancellationToken);
        try
        {
            vtt = await ConvertWithFfmpegAsync(
                ["-v", "error", "-nostdin", "-i", path, "-f", "webvtt", "pipe:1"], $"el archivo {path}", cancellationToken);
        }
        finally
        {
            _extractions.Release();
        }

        vtt ??= SubtitleConverter.AssToVtt(SubtitleConverter.Decode(await File.ReadAllBytesAsync(path, cancellationToken)));
        await StoreCacheAsync(cacheFile, itemId, track.Id, vtt, cancellationToken);
        return new SubtitleVtt(SubtitleStatus.Ok, vtt);
    }

    /// <summary>Un archivo suelto se sirve solo si existe, es pequeño y (aun siguiendo enlaces) está dentro de las carpetas de medios.</summary>
    private bool IsServable(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxExternalBytes || !_storage.IsInsideMediaRoots(path))
            {
                return false;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target is null || _storage.IsInsideMediaRoots(target.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // ───────────────────────── subidas ─────────────────────────

    /// <summary>
    /// Guarda un subtítulo que sube un usuario para el video <paramref name="itemId"/> y devuelve su pista, que desde
    /// ese momento ven todos los que pueden ver el video. Lo convierte a WebVTT (SRT en C#; ASS y SSA con ffmpeg si
    /// está, y si no con el convertidor de C#) y lo rechaza si no queda ni un cue. Sin <paramref name="language"/>
    /// el idioma sale del nombre del archivo ("Episodio 01.es.srt"); vacío, la pista queda sin idioma. Lo mismo con
    /// <paramref name="forced"/>.
    /// </summary>
    public async Task<SubtitleUpload> UploadAsync(
        Guid itemId,
        string videoPath,
        Guid userId,
        string fileName,
        byte[] content,
        string? language,
        bool? forced,
        CancellationToken cancellationToken)
    {
        if (!MediaFiles.IsVideo(videoPath) || !File.Exists(videoPath))
        {
            return new SubtitleUpload(SubtitleUploadStatus.NotVideo, Error: "Solo los videos llevan subtítulos.");
        }

        // Solo el nombre: un navegador viejo manda la ruta completa ("C:\fakepath\01.srt").
        var name = fileName.Replace('\\', '/').Split('/')[^1].Trim();
        if (name.Length == 0 || !ExternalSubtitleNames.IsSubtitleFile(name))
        {
            return Invalid("Solo se pueden subir archivos .srt, .vtt, .ass o .ssa.");
        }

        if (content.LongLength > MaxUploadBytes)
        {
            return new SubtitleUpload(SubtitleUploadStatus.TooLarge, Error: "El archivo pesa más de 8 MB.");
        }

        if (content.Length == 0)
        {
            return Invalid("El archivo está vacío.");
        }

        if (await UploadedToVttAsync(name, content, cancellationToken) is not { } vtt)
        {
            return Invalid("El archivo no tiene subtítulos que se puedan leer.");
        }

        var guess = ExternalSubtitleNames.Guess(name);
        var title = guess.Title is { Length: > MaxTitleLength } longTitle ? longTitle[..MaxTitleLength].TrimEnd() : guess.Title;
        var info = new UploadedSubtitleInfo(
            language is null ? guess.Language : UploadLanguage(language),
            forced ?? guess.IsForced,
            title,
            name,
            userId,
            DateTimeOffset.UtcNow);

        string trackId;
        await _uploadLock.WaitAsync(cancellationToken);
        try
        {
            if (_uploads.Count(itemId) >= MaxUploadsPerItem)
            {
                return new SubtitleUpload(
                    SubtitleUploadStatus.TooMany,
                    Error: $"Este video ya tiene {MaxUploadsPerItem} subtítulos subidos: borra alguno antes de subir otro.");
            }

            trackId = await _uploads.SaveAsync(itemId, info, vtt, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "No se pudo guardar el subtítulo subido en {Folder}.", _uploads.Root);
            return new SubtitleUpload(SubtitleUploadStatus.Failed, Error: "No se pudo guardar el subtítulo en el servidor.");
        }
        finally
        {
            _uploadLock.Release();
        }

        // La etiqueta depende del resto de las pistas ("Español (subido)" o "Español (subido) – Episodio 01").
        var track = (await ListAsync(itemId, videoPath, cancellationToken)).FirstOrDefault(t => t.Id == trackId);
        return track is null
            ? new SubtitleUpload(SubtitleUploadStatus.Failed, Error: "No se pudo guardar el subtítulo en el servidor.")
            : new SubtitleUpload(SubtitleUploadStatus.Ok, track);
    }

    /// <summary>Borra un subtítulo subido. Solo quien lo subió o un administrador; las pistas del archivo no se tocan.</summary>
    public async Task<SubtitleDeleteStatus> DeleteUploadAsync(
        Guid itemId, string trackId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        await _uploadLock.WaitAsync(cancellationToken);
        try
        {
            if (_uploads.Find(itemId, trackId) is not { } info)
            {
                return SubtitleDeleteStatus.NotFound;
            }

            if (!isAdmin && info.UploadedBy != userId)
            {
                return SubtitleDeleteStatus.Forbidden;
            }

            _uploads.Delete(itemId, trackId);
            return SubtitleDeleteStatus.Deleted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "No se pudo borrar el subtítulo {TrackId} de {Folder}.", trackId, _uploads.Root);
            return SubtitleDeleteStatus.Failed;
        }
        finally
        {
            _uploadLock.Release();
        }
    }

    /// <summary>Un subtítulo subido ya está en WebVTT; la ruta sale de la carpeta de subidos, nunca del cliente.</summary>
    private static async Task<SubtitleVtt> ReadUploadedAsync(SubtitleTrack track, CancellationToken cancellationToken) =>
        !string.IsNullOrEmpty(track.FilePath) && await TryReadCacheAsync(track.FilePath, cancellationToken) is { } vtt
            ? new SubtitleVtt(SubtitleStatus.Ok, vtt)
            : new SubtitleVtt(SubtitleStatus.NotFound);

    /// <summary>El archivo subido en WebVTT, o null si no trae ni un cue que se pueda leer.</summary>
    private async Task<string?> UploadedToVttAsync(string name, byte[] content, CancellationToken cancellationToken)
    {
        var text = SubtitleConverter.Decode(content);
        var vtt = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".srt" => SubtitleConverter.SrtToVtt(text),
            ".vtt" => NormalizeVtt(text),
            _ => await UploadedAssToVttAsync(name, content, text, cancellationToken),
        };
        return vtt is not null && vtt.Contains("-->", StringComparison.Ordinal) ? vtt : null;
    }

    /// <summary>Un .vtt subido tiene que empezar con WEBVTT (después del BOM); los saltos de línea quedan normales.</summary>
    private static string? NormalizeVtt(string text)
    {
        var normalized = text.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.StartsWith("WEBVTT", StringComparison.Ordinal) ? normalized : null;
    }

    /// <summary>ASS o SSA subido: ffmpeg trabaja sobre una copia temporal; si no está o falla, el convertidor de C#.</summary>
    private async Task<string> UploadedAssToVttAsync(string name, byte[] content, string text, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"mediateca-{Guid.NewGuid():N}{Path.GetExtension(name).ToLowerInvariant()}");
        string? vtt = null;
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            await _extractions.WaitAsync(cancellationToken);
            try
            {
                vtt = await ConvertWithFfmpegAsync(
                    ["-v", "error", "-nostdin", "-i", temporary, "-f", "webvtt", "pipe:1"], $"el subtítulo subido {name}", cancellationToken);
            }
            finally
            {
                _extractions.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "No se pudo preparar {Name} para ffmpeg; se usa el convertidor básico.", name);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Queda en la carpeta temporal del sistema, que se limpia sola.
            }
        }

        return vtt ?? SubtitleConverter.AssToVtt(text);
    }

    /// <summary>
    /// Idioma elegido al subir: el código de 2 letras si se conoce y, si no, el valor tal cual cuando parece un código
    /// ("ru", "pt-br"). Lo demás (vacío, texto largo, símbolos) deja la pista sin idioma.
    /// </summary>
    private static string? UploadLanguage(string value) =>
        SubtitleLanguages.Normalize(value) is { } code && LanguageCode().IsMatch(code) ? code : null;

    [GeneratedRegex(@"^[a-z]{2,3}(-[a-z0-9]{2,8})*$")]
    private static partial Regex LanguageCode();

    private static SubtitleUpload Invalid(string message) => new(SubtitleUploadStatus.Invalid, Error: message);

    // ───────────────────────── ffmpeg y caché en disco ─────────────────────────

    /// <summary>Corre ffmpeg y devuelve el WebVTT que escribe en la salida, o null si no se pudo (y lo deja en el log).</summary>
    private async Task<string?> ConvertWithFfmpegAsync(string[] arguments, string what, CancellationToken cancellationToken)
    {
        var result = await _processes.RunAsync(new ProcessRequest(_tools.Ffmpeg, arguments, _tools.ExtractionTimeout), cancellationToken);
        if (result.Outcome == ProcessOutcome.NotInstalled)
        {
            WarnOnce(ref _warnedFfmpeg, "ffmpeg", _tools.Ffmpeg, "extraer los subtítulos incrustados en los videos");
            return null;
        }

        var output = result.StandardOutput.TrimStart('\uFEFF');
        if (!result.Succeeded || !output.StartsWith("WEBVTT", StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "ffmpeg no pudo convertir {What} ({Outcome}, código {ExitCode}): {Error}",
                what, result.Outcome, result.ExitCode, Truncate(result.StandardError));
            return null;
        }

        return output;
    }

    private string CacheFileOf(Guid itemId, string trackId, long ticks) =>
        Path.Combine(CacheFolder, $"{itemId}-{trackId}-{ticks}.vtt");

    private static async Task<string?> TryReadCacheAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(file) ? await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Guarda el WebVTT para la próxima vez (primero en un archivo temporal, para que nadie lea uno a medias) y borra
    /// las versiones viejas de la misma pista. Si no se puede escribir, la solicitud responde igual.
    /// </summary>
    private async Task StoreCacheAsync(string file, Guid itemId, string trackId, string vtt, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            var temporary = $"{file}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(temporary, vtt, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, file, overwrite: true);

            foreach (var old in Directory.EnumerateFiles(CacheFolder, $"{itemId}-{trackId}-*.vtt"))
            {
                if (!string.Equals(old, file, StringComparison.Ordinal))
                {
                    File.Delete(old);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "No se pudo guardar el subtítulo en {Folder}.", CacheFolder);
        }
    }

    private void WarnOnce(ref int flag, string program, string configured, string consequence)
    {
        if (Interlocked.Exchange(ref flag, 1) == 0)
        {
            _logger.LogWarning(
                "No se encontró {Program} ({Configured}): se pierden {Consequence}. Instálalo o indica su ruta en Media:{Setting}.",
                program, configured, consequence, program == "ffprobe" ? "Ffprobe" : "Ffmpeg");
        }
    }

    private static string Truncate(string text) => text.Length <= 300 ? text.Trim() : text[..300].Trim() + "…";
}
