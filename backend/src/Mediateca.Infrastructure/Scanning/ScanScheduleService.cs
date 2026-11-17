using Mediateca.Core.Scanning;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Scanning;

/// <summary>El horario guardado, o el motivo por el que no se pudo guardar.</summary>
public sealed record ScanScheduleUpdateResult(ScanSchedule? Schedule, string? Error);

/// <summary>
/// Lee y guarda el horario del escaneo automático (una sola fila). La API lo edita y el worker lo
/// lee cada pocos segundos para aplicarlo. Los horarios se interpretan en la zona del servidor
/// (<see cref="TimeZoneInfo.Local"/>; en Docker, la variable TZ).
/// </summary>
public sealed class ScanScheduleService(MediatecaDbContext db, IOptions<ScanningOptions> options)
{
    /// <summary>Devuelve el horario guardado; si todavía no hay, lo crea con los valores por defecto.</summary>
    public async Task<ScanSchedule> GetAsync(CancellationToken cancellationToken)
    {
        var existing = await db.ScanSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == ScanSchedule.SingletonId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new ScanSchedule
        {
            Id = ScanSchedule.SingletonId,
            Enabled = options.Value.DefaultEnabled,
            CronExpression = options.Value.DefaultCron,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ScanSchedules.Add(created);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException)
        {
            // La API y el worker pueden crearla a la vez: la llave primaria deja pasar solo a uno.
            db.ChangeTracker.Clear();
            return await db.ScanSchedules.AsNoTracking()
                .FirstAsync(s => s.Id == ScanSchedule.SingletonId, cancellationToken);
        }
    }

    /// <summary>Motivo por el que la expresión no sirve, o null si es válida.</summary>
    public string? Validate(string? cronExpression) =>
        ScanScheduleRules.Validate(cronExpression, TimeZoneInfo.Local, options.Value.MinimumInterval, DateTimeOffset.UtcNow);

    /// <summary>Valida y guarda el horario. Si no es válido, no guarda nada y devuelve el error.</summary>
    public async Task<ScanScheduleUpdateResult> UpdateAsync(bool enabled, string? cronExpression, CancellationToken cancellationToken)
    {
        var error = Validate(cronExpression);
        if (error is not null)
        {
            return new ScanScheduleUpdateResult(null, error);
        }

        // Asegura que la fila exista y la vuelve a leer con rastreo para modificarla.
        await GetAsync(cancellationToken);
        var schedule = await db.ScanSchedules.FirstAsync(s => s.Id == ScanSchedule.SingletonId, cancellationToken);
        schedule.Enabled = enabled;
        schedule.CronExpression = cronExpression!.Trim();
        schedule.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new ScanScheduleUpdateResult(schedule, null);
    }

    /// <summary>El horario con la zona horaria y las próximas ejecuciones (ninguna si está desactivado).</summary>
    public ScanScheduleDescription Describe(ScanSchedule schedule)
    {
        var zone = TimeZoneInfo.Local;
        IReadOnlyList<DateTimeOffset> nextRuns = schedule.Enabled
            ? ScanScheduleRules.NextRuns(schedule.CronExpression, zone, DateTimeOffset.UtcNow, ScanScheduleRules.NextRunsCount)
            : [];
        return new ScanScheduleDescription(schedule.Enabled, schedule.CronExpression, zone.Id, nextRuns, schedule.UpdatedAt);
    }
}
