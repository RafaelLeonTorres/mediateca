using Quartz;

namespace Mediateca.Worker;

/// <summary>Identificadores de las tareas y los disparadores de Quartz del worker.</summary>
internal static class ScanScheduleKeys
{
    private const string Group = "scanning";

    /// <summary>Tarea que pide el escaneo de todas las bibliotecas. No tiene disparador propio al iniciar: lo pone <see cref="Jobs.SyncScanScheduleJob"/>.</summary>
    public static readonly JobKey ScheduledScanJob = new("scheduled-library-scan", Group);

    /// <summary>Disparador cron de <see cref="ScheduledScanJob"/>, según el horario guardado en la administración.</summary>
    public static readonly TriggerKey ScheduledScanTrigger = new("scheduled-library-scan-cron", Group);

    /// <summary>Disparador que revisa cada pocos segundos si el horario cambió.</summary>
    public static readonly TriggerKey SyncTrigger = new("sync-scan-schedule", Group);
}
