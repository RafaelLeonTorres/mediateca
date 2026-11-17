using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mediateca.Api.Libraries;

internal static class LibraryQueries
{
    /// <summary>
    /// Elementos de primer nivel (películas, series y álbumes) por biblioteca. Las temporadas, los
    /// episodios y las pistas siempre tienen padre, así que el primer nivel es ParentId nulo.
    /// </summary>
    public static async Task<Dictionary<Guid, int>> CountTopLevelAsync(
        this MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.MediaItems
            .Where(item => item.ParentId == null)
            .GroupBy(item => item.LibraryId)
            .Select(group => new { LibraryId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.LibraryId, row => row.Count);
    }
}
