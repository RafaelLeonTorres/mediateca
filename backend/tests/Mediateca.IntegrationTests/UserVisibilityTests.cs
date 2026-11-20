using System.Net;
using Mediateca.Api.Items;
using Mediateca.Api.Libraries;
using Mediateca.Api.Users;
using Mediateca.Core.Libraries;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Entregable de la fase 3: dos usuarios con bibliotecas distintas ven cada uno solo lo suyo y reciben 404 en lo
/// del otro (detalle, hijos, imagen, stream, subtítulos, reproducción y progreso). Los usuarios y sus permisos
/// se crean con la API de administración, no directo en la base de datos.
/// </summary>
public sealed class UserVisibilityTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    private const string Srt = "1\r\n00:00:01,500 --> 00:00:03,000\r\nHola, mundo\r\n";

    public Task InitializeAsync() => TestUsers.EnsureAdminAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Una biblioteca de anime con una serie, su carpeta "Lat" y un episodio con póster y subtítulo suelto.</summary>
    private sealed record Tree(Guid LibraryId, Guid SeriesId, Guid FolderId, Guid EpisodeId);

    private sealed record World(
        HttpClient Admin, AdminUserResponse AnaUser, HttpClient Ana, Tree AnaTree, AdminUserResponse BetoUser, HttpClient Beto, Tree BetoTree)
        : IDisposable
    {
        public void Dispose()
        {
            Admin.Dispose();
            Ana.Dispose();
            Beto.Dispose();
        }
    }

    private async Task<Tree> CreateTreeAsync(HttpClient admin, string seriesName)
    {
        var folder = LibraryTestSupport.CreateFolder(factory, "Anime", $"{seriesName}/Lat/01.mp4");
        LibraryTestSupport.WriteFile(folder, $"{seriesName}/folder.jpg", [1, 2, 3, 4]);
        LibraryTestSupport.WriteFile(folder, $"{seriesName}/Lat/01.es.srt", System.Text.Encoding.UTF8.GetBytes(Srt));
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName(seriesName), LibraryType.Anime, folder);
        var scan = await LibraryTestSupport.ScanAsync(admin, library.Id);
        Assert.Equal(1, scan.Added);

        var series = Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items);
        var groups = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{series.Id}/children"))!;
        var group = Assert.Single(groups);
        var episode = Assert.Single((await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{group.Id}/children"))!);
        return new Tree(library.Id, series.Id, group.Id, episode.Id);
    }

    /// <summary>Dos bibliotecas, cada una con su serie, y dos usuarios: Ana ve la primera y Beto la segunda.</summary>
    private async Task<World> CreateWorldAsync()
    {
        var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anaTree = await CreateTreeAsync(admin, "Serie de Ana");
        var betoTree = await CreateTreeAsync(admin, "Serie de Beto");

        var anaUser = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("ana"), libraryIds: [anaTree.LibraryId]);
        var betoUser = await UserTestSupport.CreateUserAsync(admin, UserTestSupport.UniqueUserName("beto"), libraryIds: [betoTree.LibraryId]);
        var ana = await TestUsers.LoginAsync(factory, anaUser.UserName);
        var beto = await TestUsers.LoginAsync(factory, betoUser.UserName);
        return new World(admin, anaUser, ana, anaTree, betoUser, beto, betoTree);
    }

    /// <summary>Las rutas de lectura de un árbol: biblioteca, serie, carpeta y episodio (detalle, hijos, imagen, stream, subtítulos y reproducción).</summary>
    private static IEnumerable<string> ReadUrls(Tree tree) =>
    [
        $"/api/libraries/{tree.LibraryId}/items",
        $"/api/libraries/{tree.LibraryId}/filters",
        $"/api/items/{tree.SeriesId}",
        $"/api/items/{tree.SeriesId}/children",
        $"/api/items/{tree.SeriesId}/image",
        $"/api/items/{tree.SeriesId}/next-up",
        $"/api/items/{tree.FolderId}",
        $"/api/items/{tree.FolderId}/children",
        $"/api/items/{tree.FolderId}/next-up",
        $"/api/items/{tree.EpisodeId}",
        $"/api/items/{tree.EpisodeId}/stream",
        $"/api/items/{tree.EpisodeId}/playback",
        $"/api/items/{tree.EpisodeId}/subtitles",
        $"/api/items/{tree.EpisodeId}/subtitles/x0.vtt",
    ];

    [Fact]
    public async Task Cada_usuario_ve_solo_sus_bibliotecas()
    {
        using var world = await CreateWorldAsync();

        var ana = (await world.Ana.GetJsonAsync<List<LibraryResponse>>("/api/libraries"))!;
        var beto = (await world.Beto.GetJsonAsync<List<LibraryResponse>>("/api/libraries"))!;
        var all = (await world.Admin.GetJsonAsync<List<LibraryResponse>>("/api/libraries"))!;

        Assert.Equal(world.AnaTree.LibraryId, Assert.Single(ana).Id);
        Assert.Equal(world.BetoTree.LibraryId, Assert.Single(beto).Id);
        // El administrador ve las dos, sin tener que asignárselas.
        Assert.Contains(all, l => l.Id == world.AnaTree.LibraryId);
        Assert.Contains(all, l => l.Id == world.BetoTree.LibraryId);

        // "Agregado recientemente": cada quien ve lo de su biblioteca y nada más.
        var anaRecent = (await world.Ana.GetJsonAsync<List<ItemSummary>>("/api/me/recently-added?limit=50"))!;
        var betoRecent = (await world.Beto.GetJsonAsync<List<ItemSummary>>("/api/me/recently-added?limit=50"))!;
        Assert.Equal(world.AnaTree.SeriesId, Assert.Single(anaRecent).Id);
        Assert.Equal(world.BetoTree.SeriesId, Assert.Single(betoRecent).Id);
    }

    [Fact]
    public async Task Lo_propio_responde_bien_y_lo_del_otro_responde_404_en_todas_las_rutas_de_lectura()
    {
        using var world = await CreateWorldAsync();

        // Lo propio funciona (el "A continuación" de una serie puede ser 204 si no queda nada).
        foreach (var url in ReadUrls(world.AnaTree))
        {
            using var own = await world.Ana.GetAsync(url);
            Assert.True(
                own.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
                $"{url} respondió {(int)own.StatusCode} a su dueña.");
        }

        // Lo del otro responde 404, como si no existiera, y a la inversa.
        foreach (var url in ReadUrls(world.BetoTree))
        {
            using var response = await world.Ana.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{url} respondió {(int)response.StatusCode} a quien no tiene acceso.");
        }

        foreach (var url in ReadUrls(world.AnaTree))
        {
            using var response = await world.Beto.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{url} respondió {(int)response.StatusCode} a quien no tiene acceso.");
        }

        // El administrador sí ve todo.
        foreach (var url in ReadUrls(world.AnaTree).Concat(ReadUrls(world.BetoTree)))
        {
            using var response = await world.Admin.GetAsync(url);
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent, $"{url} respondió {(int)response.StatusCode} al administrador.");
        }
    }

    [Fact]
    public async Task Lo_que_se_escribe_en_un_elemento_ajeno_responde_404_y_no_deja_rastro()
    {
        using var world = await CreateWorldAsync();
        var foreign = world.BetoTree;

        Assert.Equal(HttpStatusCode.NotFound, (await world.Ana.PutJsonAsync($"/api/items/{foreign.EpisodeId}/progress", new ProgressRequest(30))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await world.Ana.PostAsync($"/api/items/{foreign.EpisodeId}/played", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await world.Ana.PostAsync($"/api/items/{foreign.SeriesId}/played", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await world.Ana.DeleteAsync($"/api/items/{foreign.EpisodeId}/played")).StatusCode);

        // Beto no ve nada de eso en su biblioteca.
        var betoEpisode = (await world.Beto.GetJsonAsync<ItemDetail>($"/api/items/{foreign.EpisodeId}"))!;
        Assert.Equal(0, betoEpisode.PositionSeconds);
        Assert.False(betoEpisode.Played);
        Assert.Empty((await world.Beto.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!);
    }

    [Fact]
    public async Task El_progreso_de_cada_usuario_es_suyo_y_continuar_viendo_solo_muestra_lo_visible()
    {
        using var world = await CreateWorldAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await world.Beto.PutJsonAsync($"/api/items/{world.BetoTree.EpisodeId}/progress", new ProgressRequest(30))).StatusCode);

        var betoContinue = (await world.Beto.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!;
        Assert.Equal(world.BetoTree.EpisodeId, Assert.Single(betoContinue).Id);
        Assert.Empty((await world.Ana.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!);

        // Si a Beto le quitan la biblioteca, su progreso sigue guardado pero ya no aparece en "Continuar viendo".
        var revoked = await world.Admin.PutJsonAsync($"/api/admin/users/{world.BetoUser.Id}/libraries", new SetUserLibrariesRequest([]));
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Empty((await world.Beto.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!);
        Assert.Empty((await world.Beto.GetJsonAsync<List<LibraryResponse>>("/api/libraries"))!);

        // Y al devolvérsela, vuelve donde se quedó.
        await world.Admin.PutJsonAsync(
            $"/api/admin/users/{world.BetoUser.Id}/libraries", new SetUserLibrariesRequest([world.BetoTree.LibraryId]));
        var back = (await world.Beto.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!;
        Assert.Equal(30, Assert.Single(back).PositionSeconds);
    }

    [Fact]
    public async Task Dar_y_quitar_un_permiso_se_nota_al_instante_sin_volver_a_iniciar_sesion()
    {
        using var world = await CreateWorldAsync();
        var foreign = world.BetoTree;
        Assert.Equal(HttpStatusCode.NotFound, (await world.Ana.GetAsync($"/api/items/{foreign.EpisodeId}/stream")).StatusCode);

        // Ana recibe también la biblioteca de Beto: la ve sin cerrar sesión.
        var granted = await world.Admin.PutJsonAsync(
            $"/api/admin/users/{world.AnaUser.Id}/libraries",
            new SetUserLibrariesRequest([world.AnaTree.LibraryId, foreign.LibraryId]));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(2, (await world.Ana.GetJsonAsync<List<LibraryResponse>>("/api/libraries"))!.Count);
        Assert.Equal(HttpStatusCode.OK, (await world.Ana.GetAsync($"/api/items/{foreign.EpisodeId}/stream")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await world.Ana.GetAsync($"/api/items/{foreign.EpisodeId}/subtitles")).StatusCode);

        // Se la quitan: vuelve el 404.
        await world.Admin.PutJsonAsync(
            $"/api/admin/users/{world.AnaUser.Id}/libraries", new SetUserLibrariesRequest([world.AnaTree.LibraryId]));
        Assert.Equal(HttpStatusCode.NotFound, (await world.Ana.GetAsync($"/api/items/{foreign.EpisodeId}/stream")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await world.Ana.GetAsync($"/api/items/{foreign.SeriesId}")).StatusCode);
    }

    [Fact]
    public async Task Borrar_una_biblioteca_la_quita_de_los_permisos_de_los_usuarios()
    {
        using var world = await CreateWorldAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await world.Admin.DeleteAsync($"/api/admin/libraries/{world.AnaTree.LibraryId}")).StatusCode);

        Assert.Empty((await world.Ana.GetJsonAsync<List<LibraryResponse>>("/api/libraries"))!);
        var user = (await world.Admin.GetJsonAsync<AdminUserResponse>($"/api/admin/users/{world.AnaUser.Id}"))!;
        Assert.Empty(user.LibraryIds);
    }
}
