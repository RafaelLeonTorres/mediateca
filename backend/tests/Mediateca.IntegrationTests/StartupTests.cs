using System.Net;
using System.Net.Http.Json;
using Mediateca.Api.Setup;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>Con un PostgreSQL vacío, la app crea la base, las tablas y los roles al arrancar.</summary>
public sealed class StartupTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>
{
    [Fact]
    public async Task Health_responde_ok()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body?.Status);
    }

    [Fact]
    public async Task Crea_la_base_con_las_migraciones_los_roles_y_pg_trgm()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();

        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var roles = await db.Roles.Select(r => r.Name).ToListAsync();
        Assert.Contains("Admin", roles);
        Assert.Contains("Usuario", roles);

        var extensions = await db.Database
            .SqlQueryRaw<string>("SELECT extname AS \"Value\" FROM pg_extension")
            .ToListAsync();
        Assert.Contains("pg_trgm", extensions);
    }

    [Fact]
    public async Task Sin_usuarios_pide_el_primer_arranque()
    {
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");

        Assert.True(status?.NeedsSetup);
    }
}
