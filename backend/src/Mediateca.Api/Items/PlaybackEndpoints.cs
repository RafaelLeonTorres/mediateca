using System.Security.Claims;
using Mediateca.Core.Media;
using Mediateca.Core.Users;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mediateca.Api.Items;

/// <summary>
/// Reproducción (PLAN.md, sección 9): el stream con <c>Range</c>, el progreso, visto y no visto,
/// "Continuar viendo" y "A continuación". Todo parte de <c>VisibleItems</c>: lo que el usuario no puede
/// ver responde 404, igual que lo que no existe o no se reproduce.
/// </summary>
public static class PlaybackEndpoints
{
    private const int DefaultContinueLimit = 20;
    private const int MaxContinueLimit = 50;

    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Reproducción");

        group.MapGet("/items/{id:guid}/stream", StreamAsync)
            .WithSummary("Archivo de una película, episodio o pista, con soporte de Range (206) para adelantar y regresar.");

        group.MapGet("/items/{id:guid}/playback", PlaybackAsync)
            .WithSummary("Lo que necesita el reproductor: stream, tipo, duración, posición guardada, contexto del título y vecinos.");

        group.MapPut("/items/{id:guid}/progress", SaveProgressAsync)
            .WithSummary("Guarda la posición del usuario. Pasado el 90 % de la duración queda como visto y la posición vuelve a 0.");

        group.MapPost("/items/{id:guid}/played", MarkPlayedAsync)
            .WithSummary("Marca como visto un elemento; en una serie, temporada o álbum, todo lo que contiene.");

        group.MapDelete("/items/{id:guid}/played", MarkUnplayedAsync)
            .WithSummary("Marca como no visto un elemento (y borra su posición); en una serie, temporada o álbum, todo lo que contiene.");

        group.MapGet("/items/{id:guid}/next-up", NextUpAsync)
            .WithSummary("El episodio que sigue en una serie o carpeta (\"A continuación\"). 204 si no queda nada por ver.");

        group.MapGet("/me/continue-watching", ContinueWatchingAsync)
            .WithSummary("Películas y episodios a medias, del más reciente al más antiguo.");

        return app;
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound>> StreamAsync(
        Guid id,
        HttpContext http,
        ClaimsPrincipal user,
        MediatecaDbContext db,
        IOptions<StorageOptions> storage,
        CancellationToken cancellationToken)
    {
        var path = await db.VisibleItems(user.GetId(), user.IsAdmin())
            .OfType<PlayableItem>()
            .Where(i => i.Id == id)
            .Select(i => i.FilePath)
            .FirstOrDefaultAsync(cancellationToken);

        // La ruta sale de la base de datos, pero solo se sirve si está en una carpeta de medios y existe:
        // nunca un archivo cualquiera del servidor.
        if (string.IsNullOrEmpty(path) || !storage.Value.IsInsideMediaRoots(path) || !File.Exists(path))
        {
            return TypedResults.NotFound();
        }

        // Video o audio de un elemento protegido: se puede guardar en el navegador, no en cachés compartidas.
        http.Response.Headers.CacheControl = "private";
        return TypedResults.PhysicalFile(
            path,
            MediaFiles.ContentTypeOf(path) ?? "application/octet-stream",
            lastModified: new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero),
            enableRangeProcessing: true);
    }

    private static async Task<Results<Ok<PlaybackInfo>, NotFound>> PlaybackAsync(
        Guid id, ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetId();
        var visible = db.VisibleItems(userId, user.IsAdmin());
        var item = await visible.AsNoTracking().OfType<PlayableItem>().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        var data = await db.ItemProgress.AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == userId && d.ItemId == id, cancellationToken);
        var lookup = await PlaybackQueries.LoadAncestorsAsync(db, [item], cancellationToken);
        var chain = lookup.ChainOf(item);
        var (previous, next) = await PlaybackQueries.NeighborsAsync(visible, item, cancellationToken);

        return TypedResults.Ok(new PlaybackInfo(
            item.Id,
            ItemMapping.KindOf(item),
            item.Title,
            PlaybackQueries.SeriesTitleOf(chain),
            PlaybackQueries.ParentTitleOf(chain),
            ItemMapping.ArtistOf(item),
            item.ParentId,
            $"/api/items/{item.Id}/stream",
            MediaFiles.ContentTypeOf(item.FilePath) ?? "application/octet-stream",
            item is Movie or Episode,
            item.DurationSeconds,
            data?.PositionSeconds ?? 0,
            data?.Played ?? false,
            previous?.Id,
            next?.Id,
            next?.Title));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> SaveProgressAsync(
        Guid id,
        ProgressRequest request,
        ClaimsPrincipal user,
        MediatecaDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = user.GetId();

        // Primero el acceso: un elemento ajeno responde 404 aunque el cuerpo esté mal.
        var item = await db.VisibleItems(userId, user.IsAdmin())
            .OfType<PlayableItem>()
            .Where(i => i.Id == id)
            .Select(i => new { i.DurationSeconds })
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        if (request.PositionSeconds is not { } position || !double.IsFinite(position) || position < 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["positionSeconds"] = ["La posición tiene que ser un número de segundos, mayor o igual que cero."],
            });
        }

        var finished = PlaybackProgress.IsFinished(position, item.DurationSeconds);
        await UserItemDataStore.SaveProgressAsync(db, userId, id, position, finished, DateTimeOffset.UtcNow, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> MarkPlayedAsync(
        Guid id, ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetId();
        if (await PlayableIdsAsync(db, userId, user.IsAdmin(), id, cancellationToken) is not { } ids)
        {
            return TypedResults.NotFound();
        }

        await UserItemDataStore.MarkPlayedAsync(db, userId, ids, DateTimeOffset.UtcNow, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> MarkUnplayedAsync(
        Guid id, ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetId();
        if (await PlayableIdsAsync(db, userId, user.IsAdmin(), id, cancellationToken) is not { } ids)
        {
            return TypedResults.NotFound();
        }

        await UserItemDataStore.ClearAsync(db, userId, ids, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ResumeItem>, NoContent, NotFound>> NextUpAsync(
        Guid id, ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetId();
        var visible = db.VisibleItems(userId, user.IsAdmin());
        var container = await visible.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        // Solo una serie o una temporada (o carpeta) tiene "A continuación".
        if (container is not (Series or Season))
        {
            return TypedResults.NotFound();
        }

        var next = await PlaybackQueries.FindNextUpAsync(db, userId, visible, container, cancellationToken);
        if (next is null)
        {
            return TypedResults.NoContent();
        }

        return TypedResults.Ok(next);
    }

    private static async Task<Ok<List<ResumeItem>>> ContinueWatchingAsync(
        int? limit, ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetId();
        var count = Math.Clamp(limit ?? DefaultContinueLimit, 1, MaxContinueLimit);

        // Películas y episodios con posición guardada y sin ver, del más reciente al más antiguo.
        var rows = await (
                from data in db.ItemProgress.AsNoTracking()
                join item in db.VisibleItems(userId, user.IsAdmin()).AsNoTracking() on data.ItemId equals item.Id
                where data.UserId == userId
                      && data.PositionSeconds > 0
                      && !data.Played
                      && data.LastPlayedAt != null
                      && (item is Movie || item is Episode)
                orderby data.LastPlayedAt descending, data.ItemId
                select new { Item = item, Data = data })
            .Take(count)
            .ToListAsync(cancellationToken);

        var entries = rows
            .Select(row => ((PlayableItem)row.Item, (UserItemData?)row.Data))
            .ToList();
        return TypedResults.Ok(await PlaybackQueries.BuildResumeItemsAsync(db, entries, cancellationToken));
    }

    /// <summary>
    /// Lo que se marca como visto o no visto: el elemento mismo si se reproduce, o todo lo que se
    /// reproduce dentro de una serie, temporada o álbum. Una consulta, no una por episodio. Null si el
    /// usuario no puede ver el elemento.
    /// </summary>
    private static async Task<List<Guid>?> PlayableIdsAsync(
        MediatecaDbContext db, Guid userId, bool isAdmin, Guid id, CancellationToken cancellationToken)
    {
        var visible = db.VisibleItems(userId, isAdmin);
        var item = await visible.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        if (item is PlayableItem)
        {
            return [id];
        }

        // Serie: sus episodios directos y los de sus temporadas o carpetas. Temporada o álbum: los suyos.
        return await visible.OfType<PlayableItem>()
            .Where(p => p.ParentId == id || p.Parent!.ParentId == id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }
}
