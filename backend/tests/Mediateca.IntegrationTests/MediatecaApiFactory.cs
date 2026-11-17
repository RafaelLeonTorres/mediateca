using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Levanta la API contra un PostgreSQL vacío y desechable (Testcontainers; necesita Docker).
/// Cada clase de pruebas tiene el suyo, así no comparten datos.
/// </summary>
public sealed class MediatecaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), $"mediateca-pruebas-{Guid.NewGuid():N}");

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
            }));
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }
}
