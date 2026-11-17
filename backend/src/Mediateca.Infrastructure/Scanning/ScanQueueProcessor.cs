using Mediateca.Core.Scanning;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Scanning;

/// <summary>
/// Atiende la cola de escaneos (la tabla ScanJobs) en el worker: corre los que están en espera, uno
/// a la vez, y cuando no queda ninguno vuelve a mirar cada <see cref="ScanningOptions.QueuePollInterval"/>.
/// </summary>
public sealed class ScanQueueProcessor(
    IServiceScopeFactory scopeFactory,
    ScanJobRunner runner,
    IOptions<ScanningOptions> options,
    ILogger<ScanQueueProcessor> logger) : BackgroundService
{
    /// <summary>
    /// Los escaneos que estaban corriendo cuando el worker se detuvo ya no van a terminar: se marcan como
    /// fallidos antes de tomar trabajo nuevo. Los que siguen en la cola no se tocan: se atienden después.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await MarkInterruptedJobsAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await runner.RunNextAsync(stoppingToken))
                {
                }

                await Task.Delay(options.Value.QueuePollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // El worker se está deteniendo.
                break;
            }
            catch (Exception ex)
            {
                // Un fallo (por ejemplo, la base de datos no responde) no detiene la cola: se reintenta.
                logger.LogError(ex, "Falló la revisión de la cola de escaneos. Se reintenta en unos segundos.");
                try
                {
                    await Task.Delay(options.Value.QueuePollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task MarkInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
            DateTimeOffset? now = DateTimeOffset.UtcNow;
            var count = await db.ScanJobs
                .Where(j => j.Status == ScanStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(j => j.Status, ScanStatus.Failed)
                        .SetProperty(j => j.FinishedAt, now)
                        .SetProperty(j => j.ErrorMessage, "El worker se reinició antes de que terminara el escaneo. Vuelve a escanear la biblioteca."),
                    cancellationToken);
            if (count > 0)
            {
                logger.LogWarning("{Count} escaneos interrumpidos por un reinicio se marcaron como fallidos.", count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudieron revisar los escaneos en curso al iniciar.");
        }
    }
}
