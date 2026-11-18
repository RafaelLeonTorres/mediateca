using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Metadata;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// Refresca las series que siguen en emisión (o que todavía no salen) cada <see cref="MetadataOptions.AiringRefreshInterval"/>
/// (24 h): el siguiente episodio, el estado y los títulos nuevos. Pide los datos al proveedor ignorando la caché.
/// Una serie que falla se queda como estaba (un fallo de actualización no la baja a Error) y se reintenta en la
/// siguiente revisión; con un límite de solicitudes se detiene todo.
/// </summary>
public sealed class MetadataRefreshService(
    IServiceScopeFactory scopes,
    IEnumerable<IMetadataProvider> providers,
    ProviderStatusService status,
    IOptions<MetadataOptions> options,
    TimeProvider time,
    ILogger<MetadataRefreshService> logger)
{
    /// <summary>Refresca las series que ya tocan. Devuelve cuántas se actualizaron.</summary>
    public async Task<int> RefreshDueAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - options.Value.AiringRefreshInterval;
        List<(Guid Id, LibraryType Type)> due;
        await using (var listing = scopes.CreateAsyncScope())
        {
            var db = listing.ServiceProvider.GetRequiredService<MediatecaDbContext>();
            var rows = await db.MediaItems.AsNoTracking().OfType<Series>()
                .Where(series => series.MetadataStatus == MetadataStatus.Matched &&
                                 (series.Status == ReleaseStatus.Airing || series.Status == ReleaseStatus.Upcoming) &&
                                 (series.AniListId != null || series.ImdbId != null) &&
                                 (series.MetadataUpdatedAt == null || series.MetadataUpdatedAt < cutoff))
                .OrderBy(series => series.MetadataUpdatedAt)
                .Select(series => new { series.Id, series.Library!.Type })
                .ToListAsync(cancellationToken);
            due = rows.Select(row => (row.Id, row.Type)).ToList();
        }

        var refreshed = 0;
        foreach (var (id, type) in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Un proveedor sin clave o en pausa se salta: la siguiente revisión lo intenta de nuevo.
            var provider = providers.FirstOrDefault(p => p.Name == MetadataProviders.For(type));
            if (provider is null || !provider.IsConfigured || provider.PausedUntil is not null)
            {
                continue;
            }

            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<MetadataService>();
            try
            {
                if (await service.MatchAsync(id, refresh: true, cancellationToken) is { Status: MetadataStatus.Matched })
                {
                    refreshed++;
                }
            }
            catch (ProviderRateLimitedException ex)
            {
                await status.RecordPauseAsync(ex.Provider, time.GetUtcNow() + ex.RetryAfter, cancellationToken);
                logger.LogWarning("{Message} Se deja el refresco para la siguiente revisión.", ex.Message);
                break;
            }
            catch (ProviderQuotaExceededException ex)
            {
                await status.RecordPauseAsync(MetadataProviders.Omdb, ex.ResumesAt, cancellationToken);
                break;
            }
            catch (ProviderNotConfiguredException)
            {
                // Sin clave no hay nada que refrescar con ese proveedor; el resto de la lista sigue.
            }
            catch (MetadataProviderException ex)
            {
                logger.LogWarning(ex, "No se pudo refrescar la serie {Item}.", id);
            }
        }

        if (refreshed > 0)
        {
            logger.LogInformation("{Count} series en emisión actualizadas.", refreshed);
        }

        return refreshed;
    }
}
