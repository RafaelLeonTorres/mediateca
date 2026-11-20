using Mediateca.Core.Subtitles;
using Mediateca.Infrastructure;
using Mediateca.Infrastructure.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Levanta la API contra un PostgreSQL vacío y desechable (Testcontainers; necesita Docker), con el
/// procesador de escaneos del worker dentro del mismo proceso. Cada clase de pruebas tiene el suyo,
/// así no comparten datos.
/// </summary>
public class MediatecaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), $"mediateca-pruebas-{Guid.NewGuid():N}");
    private readonly string _mediaPath = Path.Combine(Path.GetTempPath(), $"mediateca-medios-{Guid.NewGuid():N}");
    private readonly string _drivesPath = Path.Combine(Path.GetTempPath(), $"mediateca-discos-{Guid.NewGuid():N}");

    public MediatecaApiFactory()
    {
        Directory.CreateDirectory(_mediaPath);
        Directory.CreateDirectory(_drivesPath);
    }

    /// <summary>Carpeta de medios de la prueba (<c>Storage:MediaRoots</c>): las bibliotecas tienen que quedar dentro de ella.</summary>
    public string MediaPath => _mediaPath;

    /// <summary>Carpeta de discos de la prueba (<c>Storage:DrivesPath</c>): lo que en Docker es /discos, con una subcarpeta por disco.</summary>
    public string DrivesPath => _drivesPath;

    /// <summary>
    /// Doble de ffprobe y ffmpeg: las pruebas no dependen de que FFmpeg esté instalado. Por omisión responde que
    /// no están instalados; cada prueba de subtítulos le pone su <see cref="FakeProcessRunner.Handler"/>.
    /// </summary>
    public FakeProcessRunner Processes { get; } = new();

    /// <summary>
    /// Los servidores de metadatos falsos (AniList, OMDb e imágenes): las pruebas guardan en ellos lo que quieren que
    /// responda la red, y la cola de metadatos se corre a mano con <see cref="MetadataQueueRunner"/>.
    /// </summary>
    public FakeMetadataHttp Metadata { get; } = new();

    /// <summary>La clave de OMDb de la prueba. Una subclase la deja vacía para probar la aplicación sin clave.</summary>
    protected virtual string? OmdbApiKey => "clave-de-prueba";

    /// <summary>
    /// Cada cuánto se vuelve a comprobar una sesión. Aquí casi nunca: al comprobarla la cookie se renueva, y ASP.NET
    /// marca esa respuesta "no-cache, no-store", lo que taparía los encabezados de caché que se prueban en imágenes,
    /// streams y subtítulos. Las pruebas de sesión usan <see cref="MediatecaApiFactorySesionAlInstante"/>.
    /// </summary>
    protected virtual string SessionValidationInterval => "01:00:00";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // La base "mediateca" no existe en el contenedor: la app tiene que crearla al arrancar.
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = "mediateca",
        };

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString.ConnectionString,
                ["Storage:DataPath"] = _dataPath,
                ["Storage:MediaRoots:0"] = _mediaPath,
                ["Storage:DrivesPath"] = _drivesPath,
                // Las pruebas inician sesión muchas veces desde la misma IP: con el límite real
                // (5 por minuto) la mayoría recibiría 429.
                ["Auth:LoginRateLimit:PermitLimit"] = "1000",
                ["Auth:Session:ValidationInterval"] = SessionValidationInterval,
                // Sin el SQL de cada consulta en la salida de las pruebas.
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning",
                // El procesador de escaneos de las pruebas mira la cola cada 200 ms y no cada 3 s.
                ["Scanning:QueuePollInterval"] = "00:00:00.200",
                // Los proveedores falsos responden al instante: sin esperas de 2 s entre solicitudes.
                ["Metadata:Omdb:ApiKey"] = OmdbApiKey,
                ["Metadata:AniList:RequestInterval"] = "00:00:00.001",
                ["Metadata:Omdb:RequestInterval"] = "00:00:00.001",
            }));

        // En producción los escaneos los corre el worker, un proceso aparte. Las pruebas registran el
        // mismo procesador dentro de la API de prueba, así no necesitan un segundo proceso.
        builder.ConfigureServices(services =>
        {
            services.AddScanProcessing();

            // La cola de metadatos no corre sola: las pruebas la corren con MetadataQueueRunner.RunUntilIdleAsync, así
            // saben cuándo terminó. Los clientes HTTP con nombre usan los servidores falsos.
            services.AddMetadataQueue();
            services.AddHttpClient(HttpClientNames.AniList).ConfigurePrimaryHttpMessageHandler(() => Metadata.AniList);
            services.AddHttpClient(HttpClientNames.Omdb).ConfigurePrimaryHttpMessageHandler(() => Metadata.Omdb);
            services.AddHttpClient(HttpClientNames.Images).ConfigurePrimaryHttpMessageHandler(() => Metadata.Images);
            services.RemoveAll<IProcessRunner>();
            services.AddSingleton<IProcessRunner>(Processes);
        });
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        foreach (var folder in new[] { _dataPath, _mediaPath, _drivesPath })
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}

/// <summary>La aplicación sin <c>Metadata:Omdb:ApiKey</c>: OMDb queda desactivado y nada falla.</summary>
public sealed class MediatecaApiFactorySinClaveOmdb : MediatecaApiFactory
{
    protected override string? OmdbApiKey => "";
}

/// <summary>
/// Las sesiones se comprueban en cada solicitud (y no cada 30 s): las pruebas de contraseña, rol y baja necesitan que
/// una sesión cerrada deje de valer al instante. Como cada respuesta renueva la cookie, aquí no se prueban
/// encabezados de caché.
/// </summary>
public sealed class MediatecaApiFactorySesionAlInstante : MediatecaApiFactory
{
    protected override string SessionValidationInterval => "00:00:00";
}
