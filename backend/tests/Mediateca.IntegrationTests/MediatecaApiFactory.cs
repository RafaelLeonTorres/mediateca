using Mediateca.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Levanta la API contra un PostgreSQL vacío y desechable (Testcontainers; necesita Docker), con el
/// procesador de escaneos del worker dentro del mismo proceso. Cada clase de pruebas tiene el suyo,
/// así no comparten datos.
/// </summary>
public sealed class MediatecaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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
                // Sin el SQL de cada consulta en la salida de las pruebas.
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning",
                // El procesador de escaneos de las pruebas mira la cola cada 200 ms y no cada 3 s.
                ["Scanning:QueuePollInterval"] = "00:00:00.200",
            }));

        // En producción los escaneos los corre el worker, un proceso aparte. Las pruebas registran el
        // mismo procesador dentro de la API de prueba, así no necesitan un segundo proceso.
        builder.ConfigureServices(services => services.AddScanProcessing());
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
