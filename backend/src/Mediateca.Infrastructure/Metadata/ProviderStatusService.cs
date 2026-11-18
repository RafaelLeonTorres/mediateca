using System.Text.Json;
using Mediateca.Core.Metadata;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>Estado de un proveedor para la administración: si tiene clave y hasta cuándo está en pausa.</summary>
public sealed record ProviderStatus(string Provider, string DisplayName, bool Configured, DateTimeOffset? PausedUntil, string? Message);

/// <summary>
/// El estado de los proveedores. El worker es quien pide los datos y la API quien muestra la administración, así
/// que cada pausa (límite de solicitudes, límite diario, errores seguidos) también se guarda en la caché
/// (<c>state</c>) para que la API la vea. Solo vale hasta la hora que dice.
/// </summary>
public sealed class ProviderStatusService(IEnumerable<IMetadataProvider> providers, IMetadataCache cache, TimeProvider time)
{
    private const string StateProvider = "state";

    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(2);

    private static string Key(string provider) => provider + "-pause";

    public async Task RecordPauseAsync(string provider, DateTimeOffset until, CancellationToken cancellationToken) =>
        await cache.SetAsync(StateProvider, Key(provider), JsonSerializer.Serialize(until.UtcDateTime.ToString("O")), cancellationToken);

    public async Task<IReadOnlyList<ProviderStatus>> GetAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var result = new List<ProviderStatus>();
        foreach (var provider in providers)
        {
            var pausedUntil = provider.PausedUntil;
            if (await cache.GetAsync(StateProvider, Key(provider.Name), MaxAge, cancellationToken) is { } stored &&
                TryParse(stored) is { } saved && saved > now && (pausedUntil is null || saved > pausedUntil))
            {
                pausedUntil = saved;
            }

            var message = !provider.IsConfigured
                ? new ProviderNotConfiguredException(provider.Name).Message
                : pausedUntil is not null
                    ? $"{MetadataProviders.DisplayName(provider.Name)} está en pausa; la cola sigue sola."
                    : null;
            result.Add(new ProviderStatus(
                provider.Name, MetadataProviders.DisplayName(provider.Name), provider.IsConfigured, pausedUntil, message));
        }

        return result;
    }

    private static DateTimeOffset? TryParse(string json)
    {
        try
        {
            return DateTimeOffset.TryParse(
                JsonSerializer.Deserialize<string>(json),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
