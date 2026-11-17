using Mediateca.Core.Users;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mediateca.Api.Items;

/// <summary>
/// Escrituras de <c>UserItemData</c> (progreso y "visto"). Son upserts que no fallan si dos solicitudes
/// del mismo usuario llegan juntas (el guardado de cada 10 segundos y el de salir de la página).
/// </summary>
internal static class UserItemDataStore
{
    /// <summary>Elementos por lote cuando se marca una serie completa: ni una consulta por episodio ni una gigante.</summary>
    private const int BatchSize = 500;

    /// <summary>
    /// Guarda la posición. Con <paramref name="finished"/> el elemento queda visto y la posición vuelve a 0;
    /// si no, <c>Played</c> se queda como estaba (volver a ver un episodio visto no lo "des-ve").
    /// </summary>
    public static async Task SaveProgressAsync(
        MediatecaDbContext db,
        Guid userId,
        Guid itemId,
        double positionSeconds,
        bool finished,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? playedAt = now;
        if (await UpdateProgressAsync(db, userId, itemId, positionSeconds, finished, playedAt, cancellationToken) > 0)
        {
            return;
        }

        db.ItemProgress.Add(new UserItemData
        {
            UserId = userId,
            ItemId = itemId,
            PositionSeconds = finished ? 0 : positionSeconds,
            Played = finished,
            LastPlayedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Otra solicitud creó la fila un instante antes: se actualiza esa.
            db.ChangeTracker.Clear();
            await UpdateProgressAsync(db, userId, itemId, positionSeconds, finished, playedAt, cancellationToken);
        }
    }

    /// <summary>Marca como vistos los elementos (posición 0 y fecha de reproducción de ahora), en lotes.</summary>
    public static async Task MarkPlayedAsync(
        MediatecaDbContext db,
        Guid userId,
        IReadOnlyCollection<Guid> itemIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? playedAt = now;
        foreach (var chunk in itemIds.Chunk(BatchSize))
        {
            // Listas y no arreglos: así Contains se traduce a SQL igual que en el resto de la API.
            var batch = chunk.ToList();

            // Los que ya tienen fila se actualizan con una sola instrucción...
            await MarkExistingAsync(db, userId, batch, playedAt, cancellationToken);

            // ...y a los que no, se les crea la fila de una vez.
            var existing = await db.ItemProgress.AsNoTracking()
                .Where(d => d.UserId == userId && batch.Contains(d.ItemId))
                .Select(d => d.ItemId)
                .ToListAsync(cancellationToken);
            var missing = batch.Except(existing).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            db.ItemProgress.AddRange(missing.Select(itemId => new UserItemData
            {
                UserId = userId,
                ItemId = itemId,
                PositionSeconds = 0,
                Played = true,
                LastPlayedAt = now,
            }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Otra solicitud creó alguna fila mientras tanto: se actualizan todas.
                db.ChangeTracker.Clear();
                await MarkExistingAsync(db, userId, missing, playedAt, cancellationToken);
            }

            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Marca como no vistos los elementos: se borra su fila (sin posición ni fecha), en lotes.</summary>
    public static async Task ClearAsync(
        MediatecaDbContext db, Guid userId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        foreach (var chunk in itemIds.Chunk(BatchSize))
        {
            var batch = chunk.ToList();
            await db.ItemProgress
                .Where(d => d.UserId == userId && batch.Contains(d.ItemId))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private static Task<int> UpdateProgressAsync(
        MediatecaDbContext db,
        Guid userId,
        Guid itemId,
        double positionSeconds,
        bool finished,
        DateTimeOffset? playedAt,
        CancellationToken cancellationToken)
    {
        var rows = db.ItemProgress.Where(d => d.UserId == userId && d.ItemId == itemId);
        return finished
            ? rows.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.PositionSeconds, 0d)
                    .SetProperty(d => d.Played, true)
                    .SetProperty(d => d.LastPlayedAt, playedAt),
                cancellationToken)
            : rows.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.PositionSeconds, positionSeconds)
                    .SetProperty(d => d.LastPlayedAt, playedAt),
                cancellationToken);
    }

    private static Task<int> MarkExistingAsync(
        MediatecaDbContext db,
        Guid userId,
        List<Guid> itemIds,
        DateTimeOffset? playedAt,
        CancellationToken cancellationToken) =>
        db.ItemProgress
            .Where(d => d.UserId == userId && itemIds.Contains(d.ItemId))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.PositionSeconds, 0d)
                    .SetProperty(d => d.Played, true)
                    .SetProperty(d => d.LastPlayedAt, playedAt),
                cancellationToken);
}
