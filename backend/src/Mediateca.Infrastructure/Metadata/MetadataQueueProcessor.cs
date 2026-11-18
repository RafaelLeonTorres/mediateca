using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// Atiende la cola de metadatos en el worker: procesa los elementos pendientes, uno a la vez (los proveedores ya
/// espacian sus solicitudes), y cuando no queda ninguno disponible vuelve a mirar cada
/// <see cref="MetadataOptions.QueuePollInterval"/>. Los escaneos dejan elementos nuevos en Pending: aquí se recogen.
/// </summary>
public sealed class MetadataQueueProcessor(
    MetadataQueueRunner runner,
    IOptions<MetadataOptions> options,
    ILogger<MetadataQueueProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await runner.RunNextAsync(stoppingToken))
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo (por ejemplo, la base de datos no responde) no detiene la cola: se reintenta.
                logger.LogError(ex, "Falló la revisión de la cola de metadatos. Se reintenta en unos segundos.");
            }

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
