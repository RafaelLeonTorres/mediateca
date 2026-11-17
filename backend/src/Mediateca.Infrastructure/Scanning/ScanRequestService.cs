using Mediateca.Core.Scanning;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mediateca.Infrastructure.Scanning;

/// <summary>El escaneo pedido y si ya había uno en la cola o corriendo para esa biblioteca.</summary>
public sealed record ScanRequestResult(ScanJob Job, bool AlreadyActive);

/// <summary>
/// Pone escaneos en la cola, que es la tabla ScanJobs (los atiende el worker). Pedir uno mientras otro
/// está activo devuelve el que ya existe: el índice único IX_ScanJobs_LibraryId_Active lo garantiza
/// en la base de datos, así que no hace falta ningún candado en la aplicación.
/// </summary>
public sealed class ScanRequestService(MediatecaDbContext db)
{
    // Si el escaneo que ganó la carrera ya terminó cuando se vuelve a buscar, se intenta de nuevo.
    private const int MaxAttempts = 3;

    /// <summary>Devuelve null si la biblioteca no existe.</summary>
    public async Task<ScanRequestResult?> RequestAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        if (!await db.Libraries.AnyAsync(l => l.Id == libraryId, cancellationToken))
        {
            return null;
        }

        return await EnqueueAsync(libraryId, cancellationToken);
    }

    /// <summary>Pide un escaneo de cada biblioteca y devuelve cuántos son nuevos (las que ya tenían uno activo no cuentan).</summary>
    public async Task<int> RequestAllAsync(CancellationToken cancellationToken)
    {
        var libraryIds = await db.Libraries.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        var queued = 0;
        foreach (var libraryId in libraryIds)
        {
            var result = await EnqueueAsync(libraryId, cancellationToken);
            if (!result.AlreadyActive)
            {
                queued++;
            }
        }

        return queued;
    }

    /// <summary>Indica si la biblioteca tiene un escaneo en la cola o corriendo.</summary>
    public Task<bool> HasActiveScanAsync(Guid libraryId, CancellationToken cancellationToken) =>
        db.ScanJobs.AnyAsync(
            j => j.LibraryId == libraryId && (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running),
            cancellationToken);

    private async Task<ScanRequestResult> EnqueueAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var active = await FindActiveAsync(libraryId, cancellationToken);
            if (active is not null)
            {
                return new ScanRequestResult(active, AlreadyActive: true);
            }

            var job = new ScanJob
            {
                Id = Guid.CreateVersion7(),
                LibraryId = libraryId,
                Status = ScanStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.ScanJobs.Add(job);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new ScanRequestResult(job, AlreadyActive: false);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Otra solicitud creó el escaneo justo antes: se descarta el nuestro y se busca el suyo.
                db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("No se pudo poner el escaneo en la cola.");
    }

    private Task<ScanJob?> FindActiveAsync(Guid libraryId, CancellationToken cancellationToken) =>
        db.ScanJobs
            .AsNoTracking()
            .Where(j => j.LibraryId == libraryId && (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running))
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
