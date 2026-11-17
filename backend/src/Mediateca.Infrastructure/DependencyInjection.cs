using Mediateca.Core.Scanning;
using Mediateca.Infrastructure.Identity;
using Mediateca.Infrastructure.Libraries;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Scanning;
using Mediateca.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mediateca.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Lo que comparten la API y el worker: las opciones de almacenamiento (carpeta de datos y de
    /// medios) y de escaneo, y EF Core con PostgreSQL.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.Section))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                options.DataPath = Path.GetFullPath(options.DataPath, environment.ContentRootPath);

                // Sin carpetas en la configuración, se usa el montaje de Docker.
                var roots = options.MediaRoots.Where(root => !string.IsNullOrWhiteSpace(root)).ToArray();
                options.MediaRoots = (roots.Length == 0 ? new[] { StorageOptions.DefaultMediaRoot } : roots)
                    .Select(root => MediaPaths.Normalize(Path.GetFullPath(root.Trim(), environment.ContentRootPath)))
                    .Distinct(MediaPaths.Comparer)
                    .ToArray();
            });

        services.AddOptions<ScanningOptions>().BindConfiguration(ScanningOptions.Section);

        // La cadena de conexión se lee al crear el DbContext y no al registrarlo, así las pruebas
        // de integración pueden apuntar a su propio PostgreSQL.
        services.AddDbContext<MediatecaDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Default.");
            options.UseNpgsql(connectionString).UseAsyncSeeding(DatabaseInitializer.SeedAsync);
        });

        return services;
    }

    /// <summary>
    /// Pedir escaneos y leer el horario del escaneo automático. Lo usan la API (para los botones de la
    /// administración) y el worker (para el escaneo programado). Ninguno de los dos corre escaneos aquí:
    /// solo los ponen en la cola.
    /// </summary>
    public static IServiceCollection AddScanRequests(this IServiceCollection services)
    {
        services.AddScoped<ScanRequestService>();
        services.AddScoped<ScanScheduleService>();
        return services;
    }

    /// <summary>
    /// Lo que corre los escaneos: el lector de etiquetas, el escáner y el procesador de la cola. Solo el
    /// worker lo registra (y las pruebas de integración, para no necesitar un segundo proceso).
    /// </summary>
    public static IServiceCollection AddScanProcessing(this IServiceCollection services)
    {
        services.AddSingleton<IAudioTagReader, TagLibAudioTagReader>();
        services.AddScoped<LibraryScanner>();
        services.AddSingleton<ScanJobRunner>();
        services.AddHostedService<ScanQueueProcessor>();
        return services;
    }

    /// <summary>
    /// Lo de la API: persistencia, Identity, las llaves de Data Protection y los servicios de
    /// bibliotecas. No registra el procesador de escaneos: eso es del worker (<see cref="AddScanProcessing"/>).
    /// En las siguientes fases se agregan aquí los proveedores de metadatos.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);

        services.AddIdentityCore<AppUser>(options =>
            {
                // Contraseñas largas en lugar de reglas de composición, como recomienda el NIST.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.AllowedUserNameCharacters += "áéíóúüñÁÉÍÓÚÜÑ";
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MediatecaDbContext>()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>()
            .AddSignInManager();

        // Llaves que cifran la cookie de sesión y los tokens XSRF. En la base de datos sobreviven
        // a que se recree el contenedor de la API.
        services.AddDataProtection()
            .SetApplicationName("Mediateca")
            .PersistKeysToDbContext<MediatecaDbContext>();

        // Bibliotecas y escaneo: la API pide los escaneos; los corre el worker.
        services.AddSingleton<LibraryPathValidator>();
        services.AddSingleton<FolderBrowser>();
        services.AddScanRequests();

        return services;
    }
}
