using System.Net;
using System.Net.Http.Headers;
using Mediateca.Api.Items;
using Mediateca.Api.Libraries;
using Mediateca.Core.Libraries;
using Mediateca.Infrastructure.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Los límites de los proveedores: AniList responde 429 con Retry-After y OMDb se queda sin solicitudes del día. En
/// los dos casos la cola se pausa, no insiste y deja los elementos en Pending para seguir sola después.
/// </summary>
public sealed class MetadataLimitesTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => TestUsers.EnsureAdminAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Un_429_de_AniList_pausa_la_cola_con_el_Retry_After_y_no_insiste()
    {
        factory.Metadata.AniList.Fail = () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        };
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Anime", "Limite Uno/01.mp4", "Limite Dos/01.mp4", "Limite Tres/01.mp4");
        var library = await LibraryTestSupport.CreateLibraryAsync(
            admin, LibraryTestSupport.UniqueName("Anime"), LibraryType.Anime, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        var runner = factory.Services.GetRequiredService<MetadataQueueRunner>();

        await runner.RunUntilIdleAsync(CancellationToken.None);

        // Una sola solicitud: la primera recibió el 429 y la cola dejó de pedir.
        Assert.Equal(1, factory.Metadata.AniList.RequestCount);
        var summary = (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!
            .Single(l => l.Id == library.Id).Metadata!;
        Assert.Equal(3, summary.Pending);
        Assert.Equal(0, summary.Error);
        Assert.NotNull(summary.PausedUntil);
        Assert.InRange(summary.PausedUntil!.Value, DateTimeOffset.UtcNow.AddSeconds(30), DateTimeOffset.UtcNow.AddSeconds(120));

        // Con la pausa puesta, volver a correr la cola no hace solicitudes.
        Assert.Equal(0, await runner.RunUntilIdleAsync(CancellationToken.None));
        Assert.Equal(1, factory.Metadata.AniList.RequestCount);

        // "Actualizar" tampoco espera: responde 429 enseguida.
        var item = (await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items[0];
        var refresh = await admin.PostAsync($"/api/admin/items/{item.Id}/metadata/refresh", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, refresh.StatusCode);
        Assert.Equal(1, factory.Metadata.AniList.RequestCount);
    }

    [Fact]
    public async Task El_limite_diario_de_OMDb_pausa_su_cola_hasta_el_dia_siguiente()
    {
        factory.Metadata.Omdb.Fail = () => FakeAniListHandler.Json("""{"Response":"False","Error":"Request limit reached!"}""");
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory, "Peliculas", "Primera (2001)/Primera (2001).mkv", "Segunda (2002)/Segunda (2002).mkv");
        var library = await LibraryTestSupport.CreateLibraryAsync(
            admin, LibraryTestSupport.UniqueName("Películas"), LibraryType.Movies, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        var runner = factory.Services.GetRequiredService<MetadataQueueRunner>();

        await runner.RunUntilIdleAsync(CancellationToken.None);

        Assert.Equal(1, factory.Metadata.Omdb.RequestCount);
        var summary = (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!
            .Single(l => l.Id == library.Id).Metadata!;
        Assert.Equal("omdb", summary.Provider);
        Assert.Equal(2, summary.Pending);
        Assert.Equal(0, summary.Error);
        Assert.True(summary.Configured);
        Assert.NotNull(summary.PausedUntil);
        Assert.True(summary.PausedUntil > DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.True(summary.PausedUntil <= DateTimeOffset.UtcNow.AddDays(1).AddMinutes(5));

        Assert.Equal(0, await runner.RunUntilIdleAsync(CancellationToken.None));
        Assert.Equal(1, factory.Metadata.Omdb.RequestCount);
    }
}

/// <summary>Sin <c>OMDB_API_KEY</c> OMDb queda desactivado: los elementos esperan en Pending, nada falla y la administración avisa.</summary>
public sealed class MetadataSinClaveOmdbTests(MediatecaApiFactorySinClaveOmdb factory)
    : IClassFixture<MediatecaApiFactorySinClaveOmdb>, IAsyncLifetime
{
    public Task InitializeAsync() => TestUsers.EnsureAdminAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Las_peliculas_quedan_pendientes_sin_errores_y_la_administracion_avisa_que_falta_la_clave()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas", "Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4");
        var library = await LibraryTestSupport.CreateLibraryAsync(
            admin, LibraryTestSupport.UniqueName("Películas"), LibraryType.Movies, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);

        var processed = await factory.Services.GetRequiredService<MetadataQueueRunner>().RunUntilIdleAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Equal(0, factory.Metadata.Omdb.RequestCount);
        var summary = (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!
            .Single(l => l.Id == library.Id).Metadata!;
        Assert.Equal("OMDb", summary.ProviderName);
        Assert.False(summary.Configured);
        Assert.Equal(1, summary.Pending);
        Assert.Equal(0, summary.Error);
        Assert.Contains("Falta la clave de OMDb", summary.Message);

        // El botón sigue funcionando: deja todo en la cola y dice que falta la clave.
        var fetch = await admin.PostAsync($"/api/admin/libraries/{library.Id}/metadata/fetch", null);
        Assert.Equal(HttpStatusCode.Accepted, fetch.StatusCode);
        var queue = (await fetch.ReadJsonAsync<Mediateca.Api.Metadata.MetadataQueueResponse>())!;
        Assert.Equal(1, queue.Queued);
        Assert.False(queue.Configured);
        Assert.Contains("Falta la clave de OMDb", queue.Message);

        // Identificar y Actualizar responden con el motivo en lugar de fallar.
        var movie = (await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items.Single();
        var refresh = await admin.PostAsync($"/api/admin/items/{movie.Id}/metadata/refresh", null);
        Assert.Equal(HttpStatusCode.Conflict, refresh.StatusCode);
        Assert.Contains("Falta la clave de OMDb", await refresh.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Metadata.Omdb.RequestCount);
    }

    [Fact]
    public async Task AniList_no_necesita_clave_y_sigue_funcionando()
    {
        factory.Metadata.AniList.Searches["sin clave anime"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(888001, "Sin Clave Anime", "Sin Clave Anime", 2026));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Anime", "Sin Clave Anime/01.mp4");
        var library = await LibraryTestSupport.CreateLibraryAsync(
            admin, LibraryTestSupport.UniqueName("Anime"), LibraryType.Anime, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);

        await factory.Services.GetRequiredService<MetadataQueueRunner>().RunUntilIdleAsync(CancellationToken.None);

        var summary = (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!
            .Single(l => l.Id == library.Id).Metadata!;
        Assert.Equal(1, summary.Matched);
        Assert.True(summary.Configured);
        Assert.Null(summary.Message);
    }
}
