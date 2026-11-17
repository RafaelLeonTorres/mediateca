namespace Mediateca.Infrastructure.Scanning;

/// <summary>Sección "Scanning" de la configuración: cola de escaneos y horario automático.</summary>
public sealed class ScanningOptions
{
    public const string Section = "Scanning";

    /// <summary>
    /// Horario inicial del escaneo automático (cron de Quartz, con segundos): todos los días a las 3:00.
    /// Solo se usa mientras no exista un horario guardado; después manda el de la administración.
    /// </summary>
    public string DefaultCron { get; set; } = "0 0 3 * * ?";

    /// <summary>Si el escaneo automático empieza activado.</summary>
    public bool DefaultEnabled { get; set; } = true;

    /// <summary>Cada cuánto busca el worker escaneos en la cola cuando no tiene nada que hacer.</summary>
    public TimeSpan QueuePollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Cada cuánto revisa el worker si el horario cambió en la administración.</summary>
    public TimeSpan ScheduleSyncInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Tiempo mínimo entre dos escaneos automáticos: un horario que dispare más seguido se rechaza.</summary>
    public TimeSpan MinimumInterval { get; set; } = TimeSpan.FromMinutes(5);
}
