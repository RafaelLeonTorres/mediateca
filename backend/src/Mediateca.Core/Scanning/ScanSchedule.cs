namespace Mediateca.Core.Scanning;

/// <summary>
/// Horario del escaneo automático de todas las bibliotecas. Es una sola fila (Id = 1) que el
/// administrador cambia desde la pantalla de bibliotecas y que el worker aplica sin reiniciarse.
/// </summary>
public class ScanSchedule
{
    /// <summary>Id de la única fila de la tabla.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; }

    /// <summary>Si es false, el worker no escanea solo; el horario se conserva para cuando se active.</summary>
    public bool Enabled { get; set; }

    /// <summary>Expresión cron de Quartz, con segundos (por ejemplo, <c>0 0 3 * * ?</c>).</summary>
    public required string CronExpression { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
