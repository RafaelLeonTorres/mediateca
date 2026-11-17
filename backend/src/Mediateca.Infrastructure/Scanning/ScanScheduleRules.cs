using Quartz;

namespace Mediateca.Infrastructure.Scanning;

/// <summary>
/// Reglas puras del horario del escaneo automático: validan y calculan las próximas ejecuciones de
/// una expresión cron de Quartz. Reciben la zona horaria y la hora actual, así que son deterministas.
/// </summary>
public static class ScanScheduleRules
{
    /// <summary>Largo máximo de la expresión (el de la columna en la base de datos).</summary>
    public const int MaxCronLength = 120;

    /// <summary>Cuántas ejecuciones futuras se muestran en la administración.</summary>
    public const int NextRunsCount = 3;

    // Cuántos intervalos consecutivos se revisan para el mínimo. Un horario como "0 0,1,30 * * * ?"
    // tiene un intervalo corto que no está entre las dos primeras ejecuciones.
    private const int GapsChecked = 10;

    /// <summary>Devuelve el motivo por el que el horario no sirve, o null si es válido.</summary>
    public static string? Validate(string? cronExpression, TimeZoneInfo timeZone, TimeSpan minimumInterval, DateTimeOffset now)
    {
        var text = cronExpression?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return "Escribe la expresión del horario.";
        }

        if (text.Length > MaxCronLength)
        {
            return $"La expresión puede tener hasta {MaxCronLength} caracteres.";
        }

        if (!CronExpression.TryParse(text, out _))
        {
            return "La expresión no es válida. Usa el formato de Quartz, con segundos; por ejemplo, 0 0 3 * * ? es todos los días a las 3:00.";
        }

        var runs = NextRuns(text, timeZone, now, GapsChecked + 1);
        if (runs.Count == 0)
        {
            return "El horario nunca se cumple. Elige uno que tenga próximas ejecuciones.";
        }

        for (var i = 1; i < runs.Count; i++)
        {
            if (runs[i] - runs[i - 1] < minimumInterval)
            {
                return $"Elige un horario con al menos {(int)minimumInterval.TotalMinutes} minutos entre escaneos.";
            }
        }

        return null;
    }

    /// <summary>
    /// Próximas ejecuciones después de <paramref name="after"/>, como máximo <paramref name="count"/>.
    /// Vacío si la expresión no es válida o ya no tiene más ejecuciones.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> NextRuns(string cronExpression, TimeZoneInfo timeZone, DateTimeOffset after, int count)
    {
        var text = cronExpression.Trim();
        if (!CronExpression.TryParse(text, out _))
        {
            return [];
        }

        var expression = new CronExpression(text, timeZone);
        var runs = new List<DateTimeOffset>();
        var current = after;
        while (runs.Count < count)
        {
            var next = expression.GetNextValidTimeAfter(current);
            if (next is null)
            {
                break;
            }

            runs.Add(next.Value);
            current = next.Value;
        }

        return runs;
    }
}
