namespace Mediateca.Worker;

/// <summary>
/// Último horario que el worker aplicó a Quartz. <see cref="Jobs.SyncScanScheduleJob"/> lo compara con
/// el guardado en la base de datos para volver a programar solo cuando cambió.
/// </summary>
public sealed class ScanScheduleState
{
    /// <summary>Huella del horario aplicado (<c>on|cron</c> o <c>off</c>). Null mientras no se haya aplicado ninguno.</summary>
    public string? Applied { get; set; }

    public static string KeyOf(bool enabled, string cronExpression) =>
        enabled ? $"on|{cronExpression.Trim()}" : "off";
}
