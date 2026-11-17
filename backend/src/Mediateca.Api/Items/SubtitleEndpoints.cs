using System.Security.Claims;
using Mediateca.Core.Media;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Storage;
using Mediateca.Infrastructure.Subtitles;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mediateca.Api.Items;

/// <summary>
/// Subtítulos de texto de un video (PLAN.md, secciones 9 y 10): la lista de pistas (incrustadas y archivos
/// sueltos) y cada una en WebVTT. Como el resto de la reproducción, todo parte de <c>VisibleItems</c>: lo que el
/// usuario no puede ver, lo que no existe y lo que está fuera de las carpetas de medios responde 404.
/// </summary>
public static class SubtitleEndpoints
{
    /// <summary>Tipo de contenido de un subtítulo WebVTT.</summary>
    public const string VttContentType = "text/vtt; charset=utf-8";

    public static IEndpointRouteBuilder MapSubtitleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Subtítulos");

        group.MapGet("/items/{id:guid}/subtitles", ListAsync)
            .WithSummary("Pistas de subtítulos de texto de un video: las incrustadas (con ffprobe) y los archivos sueltos junto a él. Lista vacía si no hay o si FFmpeg no está instalado.");

        group.MapGet("/items/{id:guid}/subtitles/{trackId}.vtt", VttAsync)
            .WithSummary("Una pista de subtítulos en WebVTT (text/vtt). 404 si la pista no existe; 502 si no se pudo convertir.");

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

        var tracks = await subtitles.ListAsync(path, cancellationToken);
        IReadOnlyList<SubtitleTrackResponse> response = [.. tracks.Select(track => SubtitleTrackResponse.From(id, track))];
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
