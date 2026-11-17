namespace Mediateca.Infrastructure.Scanning;

/// <summary>
/// El horario del escaneo automático tal como lo ve la administración: lo guardado, la zona horaria
/// del servidor y las próximas ejecuciones (vacías si está desactivado).
/// </summary>
public sealed record ScanScheduleDescription(
    bool Enabled,
    string CronExpression,
    string TimeZone,
    IReadOnlyList<DateTimeOffset> NextRuns,
    DateTimeOffset UpdatedAt);
