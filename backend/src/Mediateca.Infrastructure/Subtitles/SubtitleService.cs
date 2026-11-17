using System.Text;
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

/// <summary>
/// Subtítulos de un video (PLAN.md, secciones 9 y 14): lista las pistas incrustadas (con ffprobe) y los
/// archivos sueltos de la carpeta, y entrega cada una en WebVTT. Las incrustadas se extraen con ffmpeg y
/// el resultado se guarda en <c>{DataPath}/subtitles</c>, así que cada pista se extrae una sola vez. La
/// lista de cada archivo se guarda en memoria por ruta y fecha de modificación. Si ffprobe o ffmpeg no
/// están instalados no falla nada: solo faltan las pistas incrustadas, y se avisa una vez en el log.
/// </summary>
public sealed class SubtitleService
{
    /// <summary>Un subtítulo suelto más grande que esto no es un subtítulo: no se lee.</summary>
    private const long MaxExternalBytes = 8 * 1024 * 1024;

    private readonly IProcessRunner _processes;
    private readonly IMemoryCache _cache;
    private readonly MediaToolsOptions _tools;
    private readonly StorageOptions _storage;
    private readonly ILogger<SubtitleService> _logger;
    private readonly SemaphoreSlim _extractions;
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
    }

    /// <summary>Carpeta donde se guardan los subtítulos ya extraídos.</summary>
    public string CacheFolder => Path.Combine(_storage.DataPath, "subtitles");

    /// <summary>Pistas de texto del video: primero las incrustadas y después los archivos sueltos, ya con su etiqueta.</summary>
    public async Task<IReadOnlyList<SubtitleTrack>> ListAsync(string videoPath, CancellationToken cancellationToken)
    {
        if (!MediaFiles.IsVideo(videoPath) || !File.Exists(videoPath))
        {
            return [];
        }

        // La carpeta entra en la clave: un archivo .srt que se agrega o se quita cambia su fecha.
        var key = $"subtitles|{videoPath}|{File.GetLastWriteTimeUtc(videoPath).Ticks}|" +
                  $"{Directory.GetLastWriteTimeUtc(Path.GetDirectoryName(videoPath) ?? ".").Ticks}";
        if (_cache.TryGetValue(key, out IReadOnlyList<SubtitleTrack>? cached) && cached is not null)
        {
            return cached;
        }

        var (embedded, conclusive) = await ProbeEmbeddedAsync(videoPath, cancellationToken);
        var tracks = SubtitleLabels.Assign([.. embedded, .. DiscoverExternal(videoPath)]);

        // Si ffprobe falló o se pasó del tiempo, el resultado no se confía por mucho: se vuelve a intentar pronto.
        _cache.Set(key, tracks, conclusive
            ? new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(12) }
            : new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        return tracks;
    }

    /// <summary>El subtítulo <paramref name="trackId"/> del video, en WebVTT.</summary>
    public async Task<SubtitleVtt> GetVttAsync(Guid itemId, string videoPath, string trackId, CancellationToken cancellationToken)
    {
        var track = (await ListAsync(videoPath, cancellationToken)).FirstOrDefault(t => t.Id == trackId);
        if (track is null)
        {
            return new SubtitleVtt(SubtitleStatus.NotFound);
        }

        return track.Origin == SubtitleOrigin.Embedded
            ? await ExtractEmbeddedAsync(itemId, videoPath, track, cancellationToken)
            : await ReadExternalAsync(itemId, track, cancellationToken);
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

        var output = result.StandardOutput.TrimStart('﻿');
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
