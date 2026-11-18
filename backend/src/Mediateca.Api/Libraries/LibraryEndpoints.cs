using System.Security.Claims;
using Mediateca.Api.Items;
using Mediateca.Core.Media;
using Mediateca.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Mediateca.Api.Libraries;

/// <summary>Bibliotecas que el usuario puede ver y la cuadrícula de cada una.</summary>
public static class LibraryEndpoints
{
    public const int DefaultPageSize = 48;
    public const int MaxPageSize = 100;
    private const int MaxPage = 100_000;

    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/libraries").WithTags("Bibliotecas");

        group.MapGet("", ListAsync)
            .WithSummary("Bibliotecas que el usuario puede ver: todas para el administrador, las asignadas para los demás.");

        group.MapGet("/{id:guid}/items", ItemsAsync)
            .WithSummary("Películas, series o álbumes de la biblioteca, con búsqueda, filtros, orden (title, year, added) y paginación.");

        group.MapGet("/{id:guid}/filters", FiltersAsync)
            .WithSummary("Géneros y años que existen en la biblioteca, para los filtros.");

        return app;
    }

    private static async Task<Ok<List<LibraryResponse>>> ListAsync(
        ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var libraries = await db.VisibleLibraries(user.GetId(), user.IsAdmin())
            .AsNoTracking()
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);
        var counts = await db.CountTopLevelAsync(cancellationToken);

        return TypedResults.Ok(libraries
            .Select(l => new LibraryResponse(l.Id, l.Name, l.Type, counts.GetValueOrDefault(l.Id)))
            .ToList());
    }

    private static async Task<Results<Ok<ItemPage>, NotFound>> ItemsAsync(
        Guid id,
        string? search,
        string? genre,
        int? year,
        string? sort,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        MediatecaDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = user.GetId();
        var isAdmin = user.IsAdmin();
        if (!await db.VisibleLibraries(userId, isAdmin).AnyAsync(l => l.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        // Solo el primer nivel: películas, series y álbumes. Lo demás se abre desde su padre.
        var query = db.VisibleItems(userId, isAdmin)
            .AsNoTracking()
            .Where(i => i.LibraryId == id && i.ParentId == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            // También por el título original (romaji): los metadatos reemplazan el título por el de inglés.
            query = query.Where(i => EF.Functions.ILike(i.Title, pattern) ||
                                     (i.OriginalTitle != null && EF.Functions.ILike(i.OriginalTitle, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(genre))
        {
            var wantedGenre = genre.Trim();
            query = query.Where(i => i.Genres.Contains(wantedGenre));
        }

        if (year is { } wantedYear)
        {
            query = query.Where(i => i.Year == wantedYear);
        }

        // Siempre con desempate por Id: sin él, las páginas pueden repetir o saltarse elementos.
        IOrderedQueryable<MediaItem> ordered = sort?.ToLowerInvariant() switch
        {
            "year" => query.OrderByDescending(i => i.Year ?? 0).ThenBy(i => i.SortTitle).ThenBy(i => i.Id),
            "added" => query.OrderByDescending(i => i.DateAdded).ThenBy(i => i.SortTitle).ThenBy(i => i.Id),
            _ => query.OrderBy(i => i.SortTitle).ThenBy(i => i.Id),
        };

        var pageNumber = Math.Clamp(page ?? 1, 1, MaxPage);
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        var total = await query.CountAsync(cancellationToken);
        var items = await ordered
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ItemPage(items.Select(ItemMapping.ToSummary).ToList(), total, pageNumber, size));
    }

    private static async Task<Results<Ok<LibraryFiltersResponse>, NotFound>> FiltersAsync(
        Guid id, ClaimsPrincipal user, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var userId = user.GetId();
        var isAdmin = user.IsAdmin();
        if (!await db.VisibleLibraries(userId, isAdmin).AnyAsync(l => l.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var topLevel = db.VisibleItems(userId, isAdmin)
            .AsNoTracking()
            .Where(i => i.LibraryId == id && i.ParentId == null);

        var genreLists = await topLevel.Select(i => i.Genres).ToListAsync(cancellationToken);
        var genres = genreLists
            .SelectMany(list => list)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(genre => genre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var years = await topLevel
            .Where(i => i.Year != null)
            .Select(i => i.Year ?? 0)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new LibraryFiltersResponse(genres, years));
    }

    // % y _ son comodines de LIKE; con la barra invertida (el escape por defecto de PostgreSQL) se buscan tal cual.
    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
