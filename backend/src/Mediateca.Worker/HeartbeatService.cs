namespace Mediateca.Worker;

/// <summary>
/// Marca que el worker está vivo: escribe la hora (UTC) en un archivo al iniciar y cada 30 segundos.
/// El healthcheck de Docker Compose comprueba que el archivo se haya modificado hace menos de 2 minutos.
/// </summary>
public sealed class HeartbeatService(ILogger<HeartbeatService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>Archivo que revisa el healthcheck de docker-compose.yml.</summary>
    public static string FilePath { get; } = Path.Combine(Path.GetTempPath(), "mediateca-worker.alive");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await WriteAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // El worker se está deteniendo.
        }
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(FilePath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "No se pudo escribir {Path}.", FilePath);
        }
    }
}
