using System.Security.Claims;
using Mediateca.Api.Libraries;
using Mediateca.Core.Media;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mediateca.Api.Items;

/// <summary>
/// Detalle, hijos e imagen de un elemento, y "Agregado recientemente". Todo parte de
/// <c>VisibleItems</c>: un elemento de una biblioteca que el usuario no puede ver responde 404, igual
/// que uno que no existe.
/// </summary>
public static class ItemEndpoints
{
    private const int DefaultRecentLimit = 20;
    private const int MaxRecentLimit = 50;
    private const int MaxAncestors = 4;

    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Elementos");

        group.MapGet("/items/{id:guid}", GetAsync)
            .WithSummary("Detalle de un elemento, con la ruta de navegación hasta su biblioteca.");

        group.MapGet("/items/{id:guid}/children", ChildrenAsync)
            .WithSummary("Temporadas y episodios de una serie, episodios de una temporada o pistas de un álbum, en orden.");

        group.MapGet("/items/{id:guid}/image", ImageAsync)
            .WithSummary("Póster o carátula del elemento. 404 si no tiene.");

        group.MapGet("/me/recently-added", RecentlyAddedAsync)
            .WithSummary("Lo último que se agregó a las bibliotecas que el usuario puede ver.");

        return app;
    }

    private static async Task<Results<Ok<ItemDetail>, NotFound>> GetAsync(
        Guid id, ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var item = await db.VisibleItems(user.GetId(), user.IsAdmin())
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        var library = await db.Libraries.AsNoTracking()
            .Where(l => l.Id == item.LibraryId)
            .Select(l => new { l.Name, l.Type })
            .FirstAsync(cancellationToken);

        // Ruta de navegación, de la serie hacia abajo: Serie y Temporada para un episodio.
        var ancestors = new List<ItemReference>();
        var parentId = item.ParentId;
        while (parentId is { } currentId && ancestors.Count < MaxAncestors)
        {
            var parent = await db.MediaItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == currentId, cancellationToken);
            if (parent is null)
            {
                break;
            }

            ancestors.Insert(0, new ItemReference(parent.Id, ItemMapping.KindOf(parent), parent.Title));
            parentId = parent.ParentId;
        }

        return TypedResults.Ok(ItemMapping.ToDetail(item, library.Name, library.Type, ancestors));
    }

    private static async Task<Results<Ok<List<ChildItem>>, NotFound>> ChildrenAsync(
        Guid id, ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var visible = db.VisibleItems(user.GetId(), user.IsAdmin());
        if (!await visible.AnyAsync(i => i.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var children = await visible.AsNoTracking()
            .Where(i => i.ParentId == id)
            .ToListAsync(cancellationToken);

        // Cuántos episodios tiene cada temporada o carpeta, para mostrarlo en su tarjeta.
        var seasonIds = children.OfType<Season>().Select(season => season.Id).ToList();
        var counts = new Dictionary<Guid, int>();
        if (seasonIds.Count > 0)
        {
            var rows = await visible
                .Where(i => i.ParentId != null && seasonIds.Contains(i.ParentId.Value))
                .GroupBy(i => i.ParentId)
                .Select(g => new { ParentId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            counts = rows.ToDictionary(row => row.ParentId!.Value, row => row.Count);
        }

        return TypedResults.Ok(ItemMapping.OrderChildren(children)
            .Select(child => ItemMapping.ToChild(child, child is Season ? counts.GetValueOrDefault(child.Id) : null))
            .ToList());
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound>> ImageAsync(
        Guid id,
        HttpContext http,
        ClaimsPrincipal user,
        MediatecaDbContext db,
        IOptions<StorageOptions> storage,
        CancellationToken cancellationToken)
    {
        var posterPath = await db.VisibleItems(user.GetId(), user.IsAdmin())
            .Where(i => i.Id == id)
            .Select(i => i.PosterPath)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrEmpty(posterPath))
        {
            return TypedResults.NotFound();
        }

        // La ruta sale de la base de datos, pero solo se sirve si está en una carpeta de medios o en las
        // imágenes de la app: nunca un archivo cualquiera del servidor.
        var options = storage.Value;
        var allowed = options.IsInsideMediaRoots(posterPath) || MediaPaths.IsWithin(posterPath, options.ImagesPath);
        var contentType = ContentTypeOf(posterPath);
        if (!allowed || contentType is null || !File.Exists(posterPath))
        {
            return TypedResults.NotFound();
        }

        // Imagen de un elemento protegido: se guarda en el navegador pero no en cachés compartidas.
        http.Response.Headers.CacheControl = "private, max-age=3600";
        return TypedResults.PhysicalFile(
            posterPath,
            contentType,
            lastModified: new DateTimeOffset(File.GetLastWriteTimeUtc(posterPath), TimeSpan.Zero));
    }

    private static async Task<Ok<List<ItemSummary>>> RecentlyAddedAsync(
        int? limit, ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var count = Math.Clamp(limit ?? DefaultRecentLimit, 1, MaxRecentLimit);
        var items = await db.VisibleItems(user.GetId(), user.IsAdmin())
            .AsNoTracking()
            .Where(i => i.ParentId == null)
            .OrderByDescending(i => i.DateAdded)
            .ThenBy(i => i.Id)
            .Take(count)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(items.Select(ItemMapping.ToSummary).ToList());
    }

    private static string? ContentTypeOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null,
        };
}
