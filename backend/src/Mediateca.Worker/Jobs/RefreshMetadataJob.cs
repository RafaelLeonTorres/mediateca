using Mediateca.Infrastructure.Metadata;
using Quartz;

namespace Mediateca.Worker.Jobs;

/// <summary>
/// Cada <see cref="MetadataOptions.RefreshCheckInterval"/> mira si hay series en emisión cuyos metadatos tienen más de
/// 24 h y las refresca (el siguiente episodio, el estado y los títulos nuevos). La cola de series nuevas la atiende
/// el <see cref="MetadataQueueProcessor"/>; esto solo mantiene al día lo que ya está encontrado.
/// </summary>
[DisallowConcurrentExecution]
public sealed class RefreshMetadataJob(MetadataRefreshService refresh, ILogger<RefreshMetadataJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await refresh.RefreshDueAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un fallo no debe detener la tarea: se vuelve a intentar en la siguiente revisión.
            logger.LogError(ex, "Falló el refresco de los metadatos de las series en emisión.");
        }
    }
}
