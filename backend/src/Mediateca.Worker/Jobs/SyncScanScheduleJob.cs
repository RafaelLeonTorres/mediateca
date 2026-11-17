using Mediateca.Infrastructure.Scanning;
using Quartz;

namespace Mediateca.Worker.Jobs;

/// <summary>
/// Cada pocos segundos compara el horario guardado en la base de datos con el que tiene Quartz y,
/// si cambió, vuelve a programar el escaneo automático. Así lo que se guarda en la administración
/// se aplica sin reiniciar el worker.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SyncScanScheduleJob(
    ScanScheduleService schedules,
    ScanScheduleState state,
    ILogger<SyncScanScheduleJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var schedule = await schedules.GetAsync(cancellationToken);
        var key = ScanScheduleState.KeyOf(schedule.Enabled, schedule.CronExpression);
        if (key == state.Applied)
        {
            return;
        }

        // Quitar el disparador anterior no hace nada si todavía no existe.
        await context.Scheduler.UnscheduleJob(ScanScheduleKeys.ScheduledScanTrigger, cancellationToken);

        if (!schedule.Enabled)
        {
            logger.LogInformation("El escaneo automático está desactivado.");
        }
        else if (schedules.Validate(schedule.CronExpression) is { } problem)
        {
            // Un horario guardado a mano en la base de datos puede no ser válido: se avisa una vez y no se programa.
            logger.LogWarning("No se programó el escaneo automático con '{Cron}': {Problem}", schedule.CronExpression, problem);
        }
        else
        {
            var trigger = TriggerBuilder.Create()
                .WithIdentity(ScanScheduleKeys.ScheduledScanTrigger)
                .ForJob(ScanScheduleKeys.ScheduledScanJob)
                .WithCronSchedule(schedule.CronExpression.Trim())
                .Build();
            await context.Scheduler.ScheduleJob(trigger, cancellationToken);

            var zone = TimeZoneInfo.Local;
            var next = ScanScheduleRules.NextRuns(schedule.CronExpression, zone, DateTimeOffset.UtcNow, 1);
            var nextText = next.Count > 0
                ? TimeZoneInfo.ConvertTime(next[0], zone).ToString("yyyy-MM-dd HH:mm:ss zzz")
                : "ninguna";
            logger.LogInformation(
                "Escaneo automático programado con '{Cron}' (zona horaria {Zone}). Próxima ejecución: {Next}.",
                schedule.CronExpression.Trim(), zone.Id, nextText);
        }

        state.Applied = key;
    }
}
