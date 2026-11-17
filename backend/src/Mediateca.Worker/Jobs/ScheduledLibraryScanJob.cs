using Mediateca.Infrastructure.Scanning;
using Quartz;

namespace Mediateca.Worker.Jobs;

/// <summary>
/// Se dispara con el horario del escaneo automático: pone en la cola un escaneo de cada biblioteca.
/// No escanea nada aquí; de eso se encarga el <see cref="ScanQueueProcessor"/>.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ScheduledLibraryScanJob(ScanRequestService scans, ILogger<ScheduledLibraryScanJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var queued = await scans.RequestAllAsync(cancellationToken);
        logger.LogInformation("Escaneo automático: se pusieron en cola {Count} escaneos.", queued);
    }
}
