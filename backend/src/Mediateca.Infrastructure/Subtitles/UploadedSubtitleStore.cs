using System.Text;
using System.Text.Json;
using Mediateca.Core.Subtitles;

namespace Mediateca.Infrastructure.Subtitles;

/// <summary>Los datos de un subtítulo subido, que se guardan junto a su WebVTT.</summary>
internal sealed record UploadedSubtitleInfo(
    string? Language,
    bool IsForced,
    string? Title,
    string FileName,
    Guid UploadedBy,
    DateTimeOffset UploadedAt);

/// <summary>
/// Los subtítulos subidos desde el reproductor (PLAN.md, sección 9). Cada uno son dos archivos en
/// <c>{DataPath}/uploads/subtitles/{itemId}</c>: <c>{n}.vtt</c> con el subtítulo ya en WebVTT y <c>{n}.json</c> con
/// sus datos, donde <c>{n}</c> son 32 caracteres hexadecimales. El JSON se escribe al final y se borra primero:
/// un subtítulo a medias nunca aparece en la lista. Está aparte de la caché de extracciones para que vaciarla no
/// borre lo que subieron los usuarios.
/// </summary>
internal sealed class UploadedSubtitleStore(string root)
{
    /// <summary>Los ids de las pistas subidas empiezan con esta letra: <c>u3f2a…</c>.</summary>
    public const char IdPrefix = 'u';

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Root => root;

    public string FolderOf(Guid itemId) => Path.Combine(root, itemId.ToString("D"));

    /// <summary>
    /// Nombre de los archivos de una pista subida (los 32 caracteres después de la <c>u</c>), o null si el id no es
    /// de una pista subida. Solo acepta minúsculas, así cada pista tiene un único id y nunca sale una ruta rara.
    /// </summary>
    public static string? FileStemOf(string trackId)
    {
        if (trackId.Length != 33 || trackId[0] != IdPrefix)
        {
            return null;
        }

        var stem = trackId[1..];
        return Guid.TryParseExact(stem, "N", out var id) && id.ToString("N") == stem ? stem : null;
    }

    /// <summary>Las pistas subidas a un video, de la más vieja a la más nueva, todavía sin etiqueta.</summary>
    public IReadOnlyList<SubtitleTrack> List(Guid itemId)
    {
        var folder = FolderOf(itemId);
        List<string> files;
        try
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            files = [.. Directory.EnumerateFiles(folder, "*.json")];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var found = new List<(string Stem, UploadedSubtitleInfo Info, string Vtt)>();
        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var vtt = Path.Combine(folder, stem + ".vtt");
            if (FileStemOf(IdPrefix + stem) is not null && File.Exists(vtt) && ReadInfo(file) is { } info)
            {
                found.Add((stem, info, vtt));
            }
        }

        return
        [
            .. found
                .OrderBy(upload => upload.Info.UploadedAt)
                .ThenBy(upload => upload.Stem, StringComparer.Ordinal)
                .Select(upload => new SubtitleTrack(
                    IdPrefix + upload.Stem,
                    string.Empty,
                    upload.Info.Language,
                    // Un JSON editado a mano podría no traer el nombre: el códec es solo informativo.
                    ExternalSubtitleNames.CodecOf(upload.Info.FileName ?? string.Empty),
                    SubtitleOrigin.Uploaded,
                    false,
                    upload.Info.IsForced,
                    upload.Info.Title,
                    null,
                    upload.Vtt,
                    upload.Info.UploadedBy)),
        ];
    }

    /// <summary>Cuántos subtítulos tiene subidos un video.</summary>
    public int Count(Guid itemId)
    {
        var folder = FolderOf(itemId);
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json").Count() : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Los datos de una pista subida, o null si no existe.</summary>
    public UploadedSubtitleInfo? Find(Guid itemId, string trackId)
    {
        if (FileStemOf(trackId) is not { } stem)
        {
            return null;
        }

        var json = Path.Combine(FolderOf(itemId), stem + ".json");
        return File.Exists(json) ? ReadInfo(json) : null;
    }

    /// <summary>Guarda un subtítulo nuevo y devuelve el id de su pista. Lanza si no se puede escribir.</summary>
    public async Task<string> SaveAsync(Guid itemId, UploadedSubtitleInfo info, string vtt, CancellationToken cancellationToken)
    {
        var folder = FolderOf(itemId);
        Directory.CreateDirectory(folder);
        var stem = Guid.NewGuid().ToString("N");
        var vttFile = Path.Combine(folder, stem + ".vtt");
        try
        {
            await WriteAtomicallyAsync(vttFile, vtt, cancellationToken);
            await WriteAtomicallyAsync(Path.Combine(folder, stem + ".json"), JsonSerializer.Serialize(info, Json), cancellationToken);
        }
        catch
        {
            TryDelete(vttFile);
            throw;
        }

        return IdPrefix + stem;
    }

    /// <summary>Borra una pista subida (primero sus datos, así deja de listarse aunque el resto falle).</summary>
    public void Delete(Guid itemId, string trackId)
    {
        if (FileStemOf(trackId) is not { } stem)
        {
            return;
        }

        var folder = FolderOf(itemId);
        File.Delete(Path.Combine(folder, stem + ".json"));
        TryDelete(Path.Combine(folder, stem + ".vtt"));
        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Una carpeta vacía que se queda no le hace daño a nadie.
        }
    }

    private static UploadedSubtitleInfo? ReadInfo(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<UploadedSubtitleInfo>(File.ReadAllText(file, Encoding.UTF8), Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Primero en un archivo temporal, para que nadie lea uno a medias.</summary>
    private static async Task WriteAtomicallyAsync(string file, string content, CancellationToken cancellationToken)
    {
        var temporary = $"{file}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, file, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Se intentó; un archivo huérfano no se lista.
        }
    }
}
