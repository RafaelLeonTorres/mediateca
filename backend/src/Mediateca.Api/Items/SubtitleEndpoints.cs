using System.Security.Claims;
using Mediateca.Core.Media;
using Mediateca.Core.Subtitles;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Storage;
using Mediateca.Infrastructure.Subtitles;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mediateca.Api.Items;

/// <summary>
/// Subtítulos de texto de un video (PLAN.md, secciones 9 y 10): la lista de pistas (incrustadas, archivos sueltos y
/// subidos), cada una en WebVTT, y subir o borrar un archivo propio. Como el resto de la reproducción, todo parte de
/// <c>VisibleItems</c>: lo que el usuario no puede ver, lo que no existe y lo que está fuera de las carpetas de
/// medios responde 404.
/// </summary>
public static class SubtitleEndpoints
{
    /// <summary>Tipo de contenido de un subtítulo WebVTT.</summary>
    public const string VttContentType = "text/vtt; charset=utf-8";

    public static IEndpointRouteBuilder MapSubtitleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Subtítulos");

        group.MapGet("/items/{id:guid}/subtitles", ListAsync)
            .WithSummary("Pistas de subtítulos de texto de un video: las incrustadas (con ffprobe), los archivos sueltos junto a él y los subidos. Lista vacía si no hay.");

        group.MapGet("/items/{id:guid}/subtitles/{trackId}.vtt", VttAsync)
            .WithSummary("Una pista de subtítulos en WebVTT (text/vtt). 404 si la pista no existe; 502 si no se pudo convertir.");

        // El cuerpo es el archivo tal cual (no multipart): ?name= dice su nombre y, opcionales, ?language= y ?forced=.
        group.MapPost("/items/{id:guid}/subtitles", UploadAsync)
            .WithSummary("Sube un subtítulo (.srt, .vtt, .ass o .ssa, hasta 8 MB) para un video; queda para todos los que pueden verlo. El cuerpo es el archivo y ?name= su nombre. 201 con la pista; 400 si no es un subtítulo; 409 si el video ya tiene 20; 413 si pesa demasiado.");

        group.MapDelete("/items/{id:guid}/subtitles/{trackId}", DeleteAsync)
            .WithSummary("Borra un subtítulo subido: solo quien lo subió o un administrador. 204; 403 si es de otro usuario; 404 si no existe o es una pista del archivo.");

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<SubtitleTrackResponse>>, NotFound>> ListAsync(
        Guid id,
        ClaimsPrincipal user,
        MediatecaDbContext db,
        IOptions<StorageOptions> storage,
        SubtitleService subtitles,
        CancellationToken cancellationToken)
    {
        if (await FindVideoPathAsync(id, user, db, storage.Value, cancellationToken) is not { } path)
        {
            return TypedResults.NotFound();
        }

        var tracks = await subtitles.ListAsync(id, path, cancellationToken);
        IReadOnlyList<SubtitleTrackResponse> response =
            [.. tracks.Select(track => SubtitleTrackResponse.From(id, track, CanDelete(track, user)))];
        return TypedResults.Ok(response);
    }

    private static async Task<Results<ContentHttpResult, NotFound, ProblemHttpResult>> VttAsync(
        Guid id,
        string trackId,
        HttpContext http,
        ClaimsPrincipal user,
        MediatecaDbContext db,
        IOptions<StorageOptions> storage,
        SubtitleService subtitles,
        CancellationToken cancellationToken)
    {
        if (await FindVideoPathAsync(id, user, db, storage.Value, cancellationToken) is not { } path)
        {
            return TypedResults.NotFound();
        }

        var vtt = await subtitles.GetVttAsync(id, path, trackId, cancellationToken);
        switch (vtt.Status)
        {
            case SubtitleStatus.Ok:
                // Un subtítulo de un elemento protegido: el navegador puede guardarlo, una caché compartida no.
                http.Response.Headers.CacheControl = "private";
                return TypedResults.Text(vtt.Content, VttContentType);
            case SubtitleStatus.NotFound:
                return TypedResults.NotFound();
            default:
                return TypedResults.Problem(
                    title: "No se pudo extraer el subtítulo.",
                    detail: "FFmpeg no pudo convertir la pista o no está instalado en el servidor.",
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Se lee el cuerpo crudo (sin <c>IFormFile</c>, que pediría el antiforgery de formularios): la protección XSRF de
    /// siempre ya revisó la cabecera. Se corta en cuanto pasa del límite, aunque no diga su tamaño.
    /// </summary>
    private static async Task<Results<Created<SubtitleTrackResponse>, NotFound, ProblemHttpResult>> UploadAsync(
        Guid id,
        string? name,
        string? language,
        bool? forced,
        HttpContext http,
        ClaimsPrincipal user,
        MediatecaDbContext db,
        IOptions<StorageOptions> storage,
        SubtitleService subtitles,
        CancellationToken cancellationToken)
    {
        if (await FindVideoPathAsync(id, user, db, storage.Value, cancellationToken) is not { } path)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Rejected(StatusCodes.Status400BadRequest, "Falta el nombre del archivo (?name=).");
        }

        if (http.Request.ContentLength > SubtitleService.MaxUploadBytes ||
            await ReadBodyAsync(http.Request, SubtitleService.MaxUploadBytes, cancellationToken) is not { } content)
        {
            return Rejected(StatusCodes.Status413PayloadTooLarge, "El archivo pesa más de 8 MB.");
        }

        var upload = await subtitles.UploadAsync(id, path, user.GetId(), name, content, language, forced, cancellationToken);
        return upload.Status switch
        {
            SubtitleUploadStatus.Ok when upload.Track is { } track => TypedResults.Created(
                $"/api/items/{id}/subtitles/{track.Id}.vtt",
                SubtitleTrackResponse.From(id, track, canDelete: true)),
            SubtitleUploadStatus.NotVideo => TypedResults.NotFound(),
            SubtitleUploadStatus.Invalid => Rejected(StatusCodes.Status400BadRequest, upload.Error),
            SubtitleUploadStatus.TooLarge => Rejected(StatusCodes.Status413PayloadTooLarge, upload.Error),
            SubtitleUploadStatus.TooMany => Rejected(StatusCodes.Status409Conflict, upload.Error),
            _ => Rejected(StatusCodes.Status500InternalServerError, upload.Error),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        string trackId,
        ClaimsPrincipal user,
        MediatecaDbContext db,
        IOptions<StorageOptions> storage,
        SubtitleService subtitles,
        CancellationToken cancellationToken)
    {
        if (await FindVideoPathAsync(id, user, db, storage.Value, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        return await subtitles.DeleteUploadAsync(id, trackId, user.GetId(), user.IsAdmin(), cancellationToken) switch
        {
            SubtitleDeleteStatus.Deleted => TypedResults.NoContent(),
            SubtitleDeleteStatus.NotFound => TypedResults.NotFound(),
            SubtitleDeleteStatus.Forbidden => TypedResults.Problem(
                title: "No se pudo borrar el subtítulo.",
                detail: "Solo quien lo subió o un administrador puede borrarlo.",
                statusCode: StatusCodes.Status403Forbidden),
            _ => TypedResults.Problem(
                title: "No se pudo borrar el subtítulo.",
                detail: "No se pudo borrar el archivo en el servidor.",
                statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>Un subtítulo subido lo puede borrar quien lo subió o un administrador; los del archivo, nadie.</summary>
    private static bool CanDelete(SubtitleTrack track, ClaimsPrincipal user) =>
        track.Origin == SubtitleOrigin.Uploaded && (user.IsAdmin() || track.UploadedBy == user.GetId());

    private static ProblemHttpResult Rejected(int status, string? detail) =>
        TypedResults.Problem(title: "No se pudo agregar el subtítulo.", detail: detail, statusCode: status);

    /// <summary>El cuerpo completo, o null si pasa de <paramref name="limit"/> bytes.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Ruta del video de un elemento que el usuario puede ver, o null. Igual que el stream: la ruta sale de la base
    /// de datos y solo cuenta si está dentro de una carpeta de medios y el archivo existe.
    /// </summary>
    private static async Task<string?> FindVideoPathAsync(
        Guid id, ClaimsPrincipal user, MediatecaDbContext db, StorageOptions storage, CancellationToken cancellationToken)
    {
        var path = await db.VisibleItems(user.GetId(), user.IsAdmin())
            .OfType<PlayableItem>()
            .Where(i => i.Id == id)
            .Select(i => i.FilePath)
            .FirstOrDefaultAsync(cancellationToken);

        return !string.IsNullOrEmpty(path) && storage.IsInsideMediaRoots(path) && File.Exists(path) ? path : null;
    }
}
