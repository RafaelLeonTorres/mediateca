using System.Threading.RateLimiting;
using Mediateca.Core.Metadata;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// Compuerta de las solicitudes a un proveedor, compartida por todo el proceso: deja pasar una solicitud cada
/// <c>interval</c> (un <see cref="TokenBucketRateLimiter"/> de un solo token) y, además, se puede pausar hasta una
/// hora (el <c>Retry-After</c> de un 429, o el día siguiente cuando OMDb llega a su límite diario). Mientras está
/// pausada, nadie pasa.
/// </summary>
public sealed class ProviderGate : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;
    private readonly TimeProvider _time;
    private long _pausedUntilTicks;

    /// <param name="interval">Tiempo mínimo entre dos solicitudes.</param>
    /// <param name="time">Reloj (las pruebas pueden poner uno propio).</param>
    /// <param name="autoReplenishment">Falso solo en las pruebas: el token se repone a mano con <see cref="TryReplenish"/>.</param>
    public ProviderGate(TimeSpan interval, TimeProvider? time = null, bool autoReplenishment = true)
    {
        _time = time ?? TimeProvider.System;
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = interval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : interval,
            QueueLimit = 10_000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = autoReplenishment,
        });
    }

    /// <summary>Hasta cuándo está pausada, o null si no lo está.</summary>
    public DateTimeOffset? PausedUntil
    {
        get
        {
            var ticks = Interlocked.Read(ref _pausedUntilTicks);
            if (ticks == 0)
            {
                return null;
            }

            var until = new DateTimeOffset(ticks, TimeSpan.Zero);
            return until > _time.GetUtcNow() ? until : null;
        }
    }

    /// <summary>Pausa hasta esa hora. Si ya estaba pausada hasta más tarde, no cambia nada.</summary>
    public void PauseUntil(DateTimeOffset until)
    {
        var ticks = until.UtcTicks;
        long current;
        do
        {
            current = Interlocked.Read(ref _pausedUntilTicks);
            if (current >= ticks)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _pausedUntilTicks, ticks, current) != current);
    }

    public void PauseFor(TimeSpan duration) => PauseUntil(_time.GetUtcNow() + duration);

    /// <summary>Espera a que termine la pausa y a que le toque el turno. Después se puede hacer la solicitud.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (PausedUntil is { } until)
        {
            var wait = until - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, cancellationToken);
            }
        }

        using var lease = await _limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            throw new MetadataProviderException("Hay demasiadas solicitudes a los proveedores de metadatos en espera.");
        }
    }

    /// <summary>Repone el token a mano. Solo funciona con <c>autoReplenishment: false</c> (pruebas).</summary>
    public bool TryReplenish() => _limiter.TryReplenish();

    public void Dispose() => _limiter.Dispose();
}

/// <summary>Cuánto esperar cuando un proveedor responde 429 o bloquea: el <c>Retry-After</c> de la respuesta.</summary>
public static class RetryAfter
{
    private static readonly TimeSpan Minimum = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Maximum = TimeSpan.FromHours(1);

    /// <summary>
    /// Lo que dice <c>Retry-After</c> (segundos o fecha); si no está, <c>X-RateLimit-Reset</c> (fecha en segundos Unix,
    /// que usa AniList); si tampoco, <paramref name="fallback"/>. Siempre entre 1 s y 1 h.
    /// </summary>
    public static TimeSpan From(HttpResponseMessage response, DateTimeOffset now, TimeSpan fallback)
    {
        TimeSpan? wait = null;

        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            wait = delta;
        }
        else if (header?.Date is { } date)
        {
            wait = date - now;
        }
        else if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
                 long.TryParse(values.FirstOrDefault(), out var epochSeconds))
        {
            wait = DateTimeOffset.FromUnixTimeSeconds(epochSeconds) - now;
        }

        var result = wait ?? fallback;
        return result < Minimum ? Minimum : result > Maximum ? Maximum : result;
    }
}
