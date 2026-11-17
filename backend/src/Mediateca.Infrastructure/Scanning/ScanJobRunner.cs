using Mediateca.Core.Scanning;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.Infrastructure.Scanning;

/// <summary>
/// Toma el escaneo más antiguo de la cola (la tabla ScanJobs, estado Queued) y lo corre. El
/// escaneo se reclama con una actualización condicional (Queued pasa a Running solo si sigue en
/// Queued), así que dos procesadores nunca toman el mismo escaneo.
/// </summary>
public sealed class ScanJobRunner(IServiceScopeFactory scopeFactory)
{
    /// <summary>
    /// Corre un escaneo de la cola. Devuelve false si la cola está vacía; true si corrió uno o si otro
    /// procesador se le adelantó (en ambos casos conviene volver a mirar la cola).
    /// </summary>
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        var (found, claimedId) = await ClaimNextAsync(cancellationToken);
        if (!found)
        {
            return false;
        }

        if (claimedId is not { } jobId)
        {
            // Otro procesador reclamó ese escaneo antes: se vuelve a mirar la cola.
            return true;
        }

        // El escáner corre en su propio ámbito: tiene su propio DbContext y su rastreador de cambios.
        await using var scope = scopeFactory.CreateAsyncScope();
        var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
        await scanner.RunAsync(jobId, cancellationToken);
        return true;
    }

    // Found indica si había un escaneo en la cola; ClaimedId, el escaneo reclamado (null si otro se lo llevó antes).
    private async Task<(bool Found, Guid? ClaimedId)> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();

        var nextId = await db.ScanJobs
            .AsNoTracking()
            .Where(j => j.Status == ScanStatus.Queued)
            .OrderBy(j => j.CreatedAt)
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (nextId is not { } id)
        {
            return (false, null);
        }

        DateTimeOffset? now = DateTimeOffset.UtcNow;
        var claimed = await db.ScanJobs
            .Where(j => j.Id == id && j.Status == ScanStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, ScanStatus.Running)
                    .SetProperty(j => j.StartedAt, now),
                cancellationToken);

        return (true, claimed == 1 ? id : (Guid?)null);
    }
}
