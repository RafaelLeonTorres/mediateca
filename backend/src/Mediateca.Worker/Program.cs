using Mediateca.Infrastructure;
using Mediateca.Infrastructure.Metadata;
using Mediateca.Infrastructure.Scanning;
using Mediateca.Worker;
using Mediateca.Worker.Jobs;
using Quartz;

// El worker corre los escaneos de la cola (la tabla ScanJobs), programa el escaneo automático, busca los
// metadatos de lo que quedó pendiente y refresca las series en emisión (fase 4).
// No aplica migraciones: las aplica la API, y Docker Compose inicia el worker cuando la API ya está sana.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddScanRequests();
builder.Services.AddScanProcessing();
builder.Services.AddMetadataProcessing();
builder.Services.AddSingleton<ScanScheduleState>();
builder.Services.AddHostedService<HeartbeatService>();

var scanning = builder.Configuration.GetSection(ScanningOptions.Section).Get<ScanningOptions>() ?? new ScanningOptions();
var metadata = builder.Configuration.GetSection(MetadataOptions.Section).Get<MetadataOptions>() ?? new MetadataOptions();

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

    // Refresco de las series en emisión: la revisión es frecuente y barata; el 24 h lo decide MetadataRefreshService.
    quartz.ScheduleJob<RefreshMetadataJob>(trigger => trigger
        .WithIdentity("refresh-airing-metadata", "metadata")
        .StartNow()
        .WithSimpleSchedule(schedule => schedule
            .WithInterval(metadata.RefreshCheckInterval)
            .RepeatForever()));
});

// Al detenerse, espera a la tarea en curso. Las de escaneo son cortas; el refresco de metadatos recibe la
// cancelación y se detiene entre una serie y la siguiente.
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

await builder.Build().RunAsync();
