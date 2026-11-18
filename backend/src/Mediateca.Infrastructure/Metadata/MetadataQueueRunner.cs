using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Metadata;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// La cola de metadatos (PLAN.md, sección 8): la tabla es la cola. Cada llamada a <see cref="RunNextAsync"/> toma la
/// serie o película de primer nivel que sigue en Pending (la más antigua) de una biblioteca cuyo proveedor está
/// disponible, y le busca los metadatos. Un proveedor sin clave o en pausa no se toca, así que sus elementos esperan
/// sin que la cola dé vueltas. Las temporadas y los episodios no entran: heredan de su serie.
/// </summary>
public sealed class MetadataQueueRunner(
    IServiceScopeFactory scopes,
    IEnumerable<IMetadataProvider> providers,
    ProviderStatusService status,
    TimeProvider time,
    ILogger<MetadataQueueRunner> logger)
{
    // Con tantos errores seguidos (internet caído, AniList caído) se deja de intentar un rato en lugar de marcar toda la biblioteca como Error.
    private const int MaxConsecutiveErrors = 3;

    private static readonly TimeSpan ErrorPause = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, DateTimeOffset> _pausedUntil = [];
    private readonly Dictionary<string, int> _consecutiveErrors = [];

    /// <summary>Procesa un elemento. False si no había ninguno que se pudiera procesar ahora.</summary>
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        var available = providers.Where(IsAvailable).ToList();
        if (available.Count == 0)
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();

        var libraries = (await db.Libraries.AsNoTracking().Select(l => new { l.Id, l.Type }).ToListAsync(cancellationToken))
            .Where(l => available.Any(p => p.Supports(l.Type) && MetadataProviders.For(l.Type) == p.Name))
            .Select(l => l.Id)
            .ToList();
        if (libraries.Count == 0)
        {
            return false;
        }

        var itemId = await db.MediaItems.AsNoTracking()
            .Where(item => item.MetadataStatus == MetadataStatus.Pending &&
                           libraries.Contains(item.LibraryId) &&
                           (item is Series || item is Movie))
            .OrderBy(item => item.DateAdded)
            .ThenBy(item => item.Id)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (itemId is null)
        {
            return false;
        }

        var service = scope.ServiceProvider.GetRequiredService<MetadataService>();
        var providerName = await ProviderNameAsync(db, itemId.Value, cancellationToken);
        try
        {
            await service.MatchAsync(itemId.Value, refresh: false, cancellationToken);
            _consecutiveErrors[providerName] = 0;
        }
        catch (ProviderRateLimitedException ex)
        {
            await PauseAsync(providerName, time.GetUtcNow() + ex.RetryAfter, cancellationToken);
            logger.LogWarning("{Provider} pidió esperar {Wait}; la cola de metadatos se pausa.", providerName, ex.RetryAfter);
        }
        catch (ProviderQuotaExceededException ex)
        {
            await PauseAsync(providerName, ex.ResumesAt, cancellationToken);
        }
        catch (ProviderNotConfiguredException ex)
        {
            await PauseAsync(providerName, time.GetUtcNow() + ErrorPause, cancellationToken);
            logger.LogWarning("La cola de metadatos se pausa: {Message}", ex.Message);
        }
        catch (MetadataProviderException ex)
        {
            await service.MarkErrorAsync(itemId.Value, ex.Message, cancellationToken);
            logger.LogWarning(ex, "No se pudieron obtener los metadatos de {Item}.", itemId);
            await CountErrorAsync(providerName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falló la búsqueda de metadatos de {Item}.", itemId);
            await service.MarkErrorAsync(
                itemId.Value, "Falló la búsqueda de metadatos. Revisa el registro del worker.", cancellationToken);
            await CountErrorAsync(providerName, cancellationToken);
        }

        return true;
    }

    /// <summary>Procesa la cola hasta que no quede nada que se pueda procesar ahora. Lo usan las pruebas; el worker usa <see cref="RunNextAsync"/>.</summary>
    public async Task<int> RunUntilIdleAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        while (await RunNextAsync(cancellationToken))
        {
            count++;
        }

        return count;
    }

    private bool IsAvailable(IMetadataProvider provider) =>
        provider.IsConfigured &&
        provider.PausedUntil is null &&
        !(_pausedUntil.TryGetValue(provider.Name, out var until) && until > time.GetUtcNow());

    private async Task PauseAsync(string provider, DateTimeOffset until, CancellationToken cancellationToken)
    {
        _pausedUntil[provider] = until;
        await status.RecordPauseAsync(provider, until, cancellationToken);
    }

    private async Task CountErrorAsync(string provider, CancellationToken cancellationToken)
    {
        var count = _consecutiveErrors.GetValueOrDefault(provider) + 1;
        _consecutiveErrors[provider] = count;
        if (count >= MaxConsecutiveErrors)
        {
            _consecutiveErrors[provider] = 0;
            await PauseAsync(provider, time.GetUtcNow() + ErrorPause, cancellationToken);
            logger.LogWarning(
                "{Count} errores seguidos con {Provider}: la cola de metadatos espera {Wait} antes de seguir.",
                count, provider, ErrorPause);
        }
    }

    private static async Task<string> ProviderNameAsync(MediatecaDbContext db, Guid itemId, CancellationToken cancellationToken)
    {
        var type = await db.MediaItems.AsNoTracking()
            .Where(item => item.Id == itemId)
            .Select(item => item.Library!.Type)
            .FirstAsync(cancellationToken);
        return MetadataProviders.For(type) ?? "";
    }
}
