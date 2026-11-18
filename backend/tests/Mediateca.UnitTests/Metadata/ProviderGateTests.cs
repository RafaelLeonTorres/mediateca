using System.Net;
using System.Net.Http.Headers;
using Mediateca.Infrastructure.Metadata;

namespace Mediateca.UnitTests.Metadata;

/// <summary>El limitador compartido (1 solicitud cada 2 s en AniList) y el cálculo de Retry-After.</summary>
public class ProviderGateTests
{
    /// <summary>Un reloj que solo avanza cuando la prueba lo dice; no crea temporizadores.</summary>
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan time) => _now += time;
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Sin reposición automática, el turno vuelve solo con <see cref="ProviderGate.TryReplenish"/>, y el balde se
    /// repone según el tiempo que pasó desde la última vez (no un turno entero por llamada): antes de un intervalo
    /// completo daría una fracción de turno y nadie pasaría. Por eso el intervalo es corto y las pruebas lo esperan.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    /// <summary>Un poco más que un intervalo, para que la reposición dé un turno completo.</summary>
    private static Task UnIntervaloAsync() => Task.Delay(Interval + TimeSpan.FromMilliseconds(50));

    [Fact]
    public async Task La_primera_solicitud_pasa_enseguida_y_la_segunda_espera_a_que_se_reponga_el_turno()
    {
        using var gate = new ProviderGate(Interval, TimeProvider.System, autoReplenishment: false);

        await gate.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        var segunda = gate.WaitAsync(CancellationToken.None);

        // Ya pasó el intervalo, pero sin reponer el turno la segunda sigue esperando.
        await UnIntervaloAsync();
        Assert.False(segunda.IsCompleted);

        Assert.True(gate.TryReplenish());
        await segunda.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Las_solicitudes_en_espera_salen_en_orden()
    {
        using var gate = new ProviderGate(Interval, TimeProvider.System, autoReplenishment: false);
        await gate.WaitAsync(CancellationToken.None);

        var orden = new List<int>();
        var primera = gate.WaitAsync(CancellationToken.None).ContinueWith(_ => orden.Add(1));
        await Task.Delay(50);
        var segunda = gate.WaitAsync(CancellationToken.None).ContinueWith(_ => orden.Add(2));

        await UnIntervaloAsync();
        gate.TryReplenish();
        await primera.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(segunda.IsCompleted);

        await UnIntervaloAsync();
        gate.TryReplenish();
        await segunda.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 1, 2 }, orden);
    }

    [Fact]
    public async Task Cancelar_saca_a_la_solicitud_de_la_espera()
    {
        using var gate = new ProviderGate(TimeSpan.FromSeconds(2), TimeProvider.System, autoReplenishment: false);
        await gate.WaitAsync(CancellationToken.None);
        using var cancelar = new CancellationTokenSource();

        var espera = gate.WaitAsync(cancelar.Token);
        await cancelar.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => espera.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Una_pausa_dura_hasta_la_hora_indicada()
    {
        var reloj = new ManualTime(Start);
        using var gate = new ProviderGate(TimeSpan.FromSeconds(2), reloj, autoReplenishment: false);
        Assert.Null(gate.PausedUntil);

        gate.PauseFor(TimeSpan.FromSeconds(60));

        Assert.Equal(Start.AddSeconds(60), gate.PausedUntil);
        reloj.Advance(TimeSpan.FromSeconds(59));
        Assert.NotNull(gate.PausedUntil);
        reloj.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(gate.PausedUntil);
    }

    [Fact]
    public void Una_pausa_mas_corta_no_acorta_la_que_ya_hay()
    {
        var reloj = new ManualTime(Start);
        using var gate = new ProviderGate(TimeSpan.FromSeconds(2), reloj, autoReplenishment: false);

        gate.PauseFor(TimeSpan.FromMinutes(10));
        gate.PauseFor(TimeSpan.FromSeconds(5));

        Assert.Equal(Start.AddMinutes(10), gate.PausedUntil);
    }

    [Fact]
    public async Task Con_la_pausa_puesta_la_solicitud_no_pasa_hasta_que_termina()
    {
        using var gate = new ProviderGate(TimeSpan.FromMilliseconds(1), TimeProvider.System);
        gate.PauseFor(TimeSpan.FromMilliseconds(400));

        var antes = DateTimeOffset.UtcNow;
        await gate.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(DateTimeOffset.UtcNow - antes >= TimeSpan.FromMilliseconds(300));
        Assert.Null(gate.PausedUntil);
    }

    [Fact]
    public void Retry_After_en_segundos()
    {
        using var respuesta = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        respuesta.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));

        Assert.Equal(TimeSpan.FromSeconds(45), RetryAfter.From(respuesta, Start, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Retry_After_como_fecha()
    {
        using var respuesta = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        respuesta.Headers.RetryAfter = new RetryConditionHeaderValue(Start.AddMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), RetryAfter.From(respuesta, Start, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Sin_Retry_After_se_usa_X_RateLimit_Reset_de_AniList()
    {
        using var respuesta = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        respuesta.Headers.Add("X-RateLimit-Reset", Start.AddSeconds(30).ToUnixTimeSeconds().ToString());

        Assert.Equal(TimeSpan.FromSeconds(30), RetryAfter.From(respuesta, Start, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Sin_ninguna_cabecera_se_usa_el_valor_por_omision()
    {
        using var respuesta = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        Assert.Equal(TimeSpan.FromSeconds(60), RetryAfter.From(respuesta, Start, TimeSpan.FromSeconds(60)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 10)]
    [InlineData(36000, 3600)]
    public void La_espera_queda_entre_un_segundo_y_una_hora(int segundos, int esperados)
    {
        using var respuesta = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        respuesta.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(segundos));

        Assert.Equal(TimeSpan.FromSeconds(esperados), RetryAfter.From(respuesta, Start, TimeSpan.FromSeconds(60)));
    }
}
