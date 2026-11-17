using System.Net;
using Mediateca.Api.Libraries;
using Mediateca.Core.Libraries;
using Mediateca.Core.Scanning;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>POST /api/admin/libraries/scan-all: un escaneo en la cola por biblioteca, sin duplicar los activos.</summary>
public sealed class ScanAllTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => TestUsers.EnsureAdminAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Escanear_todas_pone_un_escaneo_por_biblioteca_y_no_duplica_el_que_ya_esta_activo()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var first = await CreateLibraryAsync(admin, "Primera");
        var second = await CreateLibraryAsync(admin, "Segunda");
        var busy = await CreateLibraryAsync(admin, "Ocupada");

        // La biblioteca "Ocupada" ya tiene un escaneo corriendo. Se crea directo en la base de datos para
        // que la prueba no dependa de qué tan rápido lo atienda el procesador.
        var runningId = await InsertRunningScanAsync(busy.Id);

        var response = await admin.PostAsync("/api/admin/libraries/scan-all", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = (await response.ReadJsonAsync<ScanAllResponse>())!;
        Assert.Equal(2, result.Queued);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
            Assert.Equal(1, await db.ScanJobs.CountAsync(j => j.LibraryId == first.Id));
            Assert.Equal(1, await db.ScanJobs.CountAsync(j => j.LibraryId == second.Id));

            var busyJob = await db.ScanJobs.SingleAsync(j => j.LibraryId == busy.Id);
            Assert.Equal(runningId, busyJob.Id);
            Assert.Equal(ScanStatus.Running, busyJob.Status);
        }

        // Las dos bibliotecas nuevas se escanean solas: las atiende el procesador de la cola.
        await WaitUntilCompletedAsync(admin, first.Id);
        await WaitUntilCompletedAsync(admin, second.Id);
    }

    private async Task<AdminLibraryResponse> CreateLibraryAsync(HttpClient admin, string name)
    {
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas", $"{name} (2000)/{name} (2000).mkv");
        return await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName(name), LibraryType.Movies, folder);
    }

    private async Task<Guid> InsertRunningScanAsync(Guid libraryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        var job = new ScanJob
        {
            Id = Guid.CreateVersion7(),
            LibraryId = libraryId,
            Status = ScanStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.ScanJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task WaitUntilCompletedAsync(HttpClient admin, Guid libraryId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var libraries = (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!;
            var status = libraries.Single(l => l.Id == libraryId).LastScan?.Status;
            if (status == ScanStatus.Completed)
            {
                return;
            }

            Assert.NotEqual(ScanStatus.Failed, status);
            await Task.Delay(100);
        }

        throw new TimeoutException($"El escaneo de la biblioteca {libraryId} no terminó en 60 segundos.");
    }
}
