using Mediateca.Core.Metadata;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// La caché de metadatos (tabla <c>MetadataCache</c>, con el JSON en una columna jsonb). Es un servicio único
/// que abre su propio ámbito en cada llamada: los proveedores también son únicos y no pueden tener un
/// <c>DbContext</c>.
/// </summary>
public sealed class MetadataCacheStore(IServiceScopeFactory scopes, TimeProvider time) : IMetadataCache
{
    public async Task<string?> GetAsync(string provider, string key, TimeSpan maxAge, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        var oldest = time.GetUtcNow() - maxAge;
        return await db.MetadataCache.AsNoTracking()
            .Where(entry => entry.Provider == provider && entry.ExternalId == key && entry.FetchedAt >= oldest)
            .Select(entry => entry.Payload)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SetAsync(string provider, string key, string payload, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        var now = time.GetUtcNow();

        var entry = await db.MetadataCache
            .FirstOrDefaultAsync(e => e.Provider == provider && e.ExternalId == key, cancellationToken);
        if (entry is null)
        {
            db.MetadataCache.Add(new MetadataCacheEntry
            {
                Provider = provider,
                ExternalId = key,
                Payload = payload,
                FetchedAt = now,
            });
        }
        else
        {
            entry.Payload = payload;
            entry.FetchedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Otra solicitud guardó la misma llave justo antes (la llave compuesta es única): se actualiza esa fila.
            db.ChangeTracker.Clear();
            await db.MetadataCache
                .Where(e => e.Provider == provider && e.ExternalId == key)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(e => e.Payload, payload)
                        .SetProperty(e => e.FetchedAt, now),
                    cancellationToken);
        }
    }

    /// <summary>Llave corta y estable para una búsqueda: la columna ExternalId admite 50 caracteres.</summary>
    public static string SearchKey(string text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
