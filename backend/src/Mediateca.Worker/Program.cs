using Mediateca.Infrastructure;
using Mediateca.Infrastructure.Scanning;
using Mediateca.Worker;
using Mediateca.Worker.Jobs;
using Quartz;

// El worker corre los escaneos de la cola (la tabla ScanJobs) y programa el escaneo automático.
// No aplica migraciones: las aplica la API, y Docker Compose inicia el worker cuando la API ya está sana.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddScanRequests();
builder.Services.AddScanProcessing();
builder.Services.AddSingleton<ScanScheduleState>();
builder.Services.AddHostedService<HeartbeatService>();

var scanning = builder.Configuration.GetSection(ScanningOptions.Section).Get<ScanningOptions>() ?? new ScanningOptions();

builder.AddQuartz(quartz =>
{
    // La tarea del escaneo automático no tiene disparador al iniciar: SyncScanScheduleJob le pone el
    // cron guardado en la administración y lo cambia cuando el administrador lo edita.
    quartz.AddJob<ScheduledLibraryScanJob>(job => job
        .WithIdentity(ScanScheduleKeys.ScheduledScanJob)
        .StoreDurably());

    quartz.ScheduleJob<SyncScanScheduleJob>(trigger => trigger
        .WithIdentity(ScanScheduleKeys.SyncTrigger)
        .StartNow()
        .WithSimpleSchedule(schedule => schedule
            .WithInterval(scanning.ScheduleSyncInterval)
            .RepeatForever()));
});

// Al detenerse, espera a que termine la tarea en curso (son cortas: solo ponen escaneos en la cola).
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

await builder.Build().RunAsync();
