using Mediateca.Api.Metadata;
using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Metadata;
using Mediateca.Infrastructure.Metadata;
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

    /// <summary>
    /// Contadores de metadatos por biblioteca (solo series y películas de primer nivel) y el estado de su proveedor.
    /// Las bibliotecas de música no tienen y no aparecen en el resultado.
    /// </summary>
    public static async Task<Dictionary<Guid, MetadataSummary>> MetadataSummariesAsync(
        this MediatecaDbContext db,
        ProviderStatusService providers,
        IEnumerable<Library> libraries,
        CancellationToken cancellationToken)
    {
        var rows = await db.MediaItems.AsNoTracking()
            .Where(item => item.ParentId == null && (item is Series || item is Movie))
            .GroupBy(item => new { item.LibraryId, item.MetadataStatus })
            .Select(group => new { group.Key.LibraryId, group.Key.MetadataStatus, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var statuses = await providers.GetAsync(cancellationToken);

        var result = new Dictionary<Guid, MetadataSummary>();
        foreach (var library in libraries)
        {
            if (MetadataProviders.For(library.Type) is not { } providerName)
            {
                continue;
            }

            int Count(MetadataStatus status) => rows
                .Where(row => row.LibraryId == library.Id && row.MetadataStatus == status)
                .Sum(row => row.Count);

            var provider = statuses.FirstOrDefault(p => p.Provider == providerName);
            result[library.Id] = new MetadataSummary(
                providerName,
                MetadataProviders.DisplayName(providerName),
                Count(MetadataStatus.Pending),
                Count(MetadataStatus.Matched),
                Count(MetadataStatus.NotFound),
                Count(MetadataStatus.Error),
                provider?.Configured ?? false,
                provider?.PausedUntil,
                provider?.Message);
        }

        return result;
    }
}
