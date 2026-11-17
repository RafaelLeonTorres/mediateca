using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Mediateca.Api.Items;
using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Users;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Reproducción (PLAN.md, sección 9): el stream con Range, el progreso y la regla del 90 %, visto y no
/// visto, "Continuar viendo", "A continuación" y los vecinos del reproductor, con árboles de archivos
/// como los de la biblioteca real (carpetas "Lat" y "Sub").
/// </summary>
public sealed class PlaybackTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    /// <summary>Duración con la que se prueba la regla del 90 %: el escáner no puede leerla de archivos vacíos.</summary>
    private const double TestDuration = 100;

    public Task InitializeAsync() => TestUsers.EnsureAdminAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Una serie "Mi Anime" con las carpetas Lat (3 episodios) y Sub (2), y sus imágenes.</summary>
    private sealed record AnimeLibrary(
        string Folder,
        Guid LibraryId,
        Guid SeriesId,
        ChildItem Lat,
        ChildItem Sub,
        IReadOnlyList<ChildItem> LatEpisodes,
        IReadOnlyList<ChildItem> SubEpisodes);

    private async Task<AnimeLibrary> CreateAnimeAsync(HttpClient admin, double? duration = TestDuration)
    {
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Anime",
            "Mi Anime/Lat/01.mp4",
            "Mi Anime/Lat/02.mp4",
            "Mi Anime/Lat/03.mp4",
            "Mi Anime/Sub/01.mp4",
            "Mi Anime/Sub/02.mp4");
        LibraryTestSupport.WriteFile(folder, "Mi Anime/folder.jpg", [1, 2, 3, 4]);
        LibraryTestSupport.WriteFile(folder, "Mi Anime/Lat/01-thumb.jpg", [1, 2, 3, 4]);
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Anime"), LibraryType.Anime, folder);
        var scan = await LibraryTestSupport.ScanAsync(admin, library.Id);
        Assert.Equal(5, scan.Added);
        await SetDurationsAsync(library.Id, duration);

        var series = Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items);
        var groups = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{series.Id}/children"))!;
        Assert.Equal(new[] { "Lat", "Sub" }, groups.Select(g => g.Title).ToArray());
        var lat = await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{groups[0].Id}/children");
        var sub = await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{groups[1].Id}/children");
        return new AnimeLibrary(folder, library.Id, series.Id, groups[0], groups[1], lat!, sub!);
    }

    private async Task SetDurationsAsync(Guid libraryId, double? seconds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        var items = await db.MediaItems.OfType<PlayableItem>().Where(i => i.LibraryId == libraryId).ToListAsync();
        foreach (var item in items)
        {
            item.DurationSeconds = seconds;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Crea un usuario nuevo (sus datos de reproducción empiezan vacíos) con acceso a esas bibliotecas.</summary>
    private async Task<HttpClient> LoginNewUserAsync(params Guid[] libraryIds)
    {
        var userName = $"lector-{Guid.NewGuid():N}";
        await TestUsers.EnsureUserAsync(factory, userName);
        var userId = await TestUsers.GetUserIdAsync(factory, userName);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
            foreach (var libraryId in libraryIds)
            {
                db.LibraryAccess.Add(new UserLibraryAccess { UserId = userId, LibraryId = libraryId });
            }

            await db.SaveChangesAsync();
        }

        return await TestUsers.LoginAsync(factory, userName);
    }

    private static Task<HttpResponseMessage> PutProgressAsync(HttpClient client, Guid id, double seconds) =>
        client.PutJsonAsync($"/api/items/{id}/progress", new ProgressRequest(seconds));

    private static async Task<ResumeItem?> NextUpAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/api/items/{id}/next-up");
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<ResumeItem>();
    }

    private static Task<List<ChildItem>?> ChildrenAsync(HttpClient client, Guid id) =>
        client.GetJsonAsync<List<ChildItem>>($"/api/items/{id}/children");

    [Fact]
    public async Task El_stream_responde_206_con_Range_y_los_encabezados_correctos()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var bytes = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray();
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas");
        LibraryTestSupport.WriteFile(folder, "Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4", bytes);
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Películas"), LibraryType.Movies, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        var movie = Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items);

        // Los primeros 100 bytes.
        using var first = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{movie.Id}/stream");
        first.Headers.Range = new RangeHeaderValue(0, 99);
        using var partial = await admin.SendAsync(first);

        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(new ContentRangeHeaderValue(0, 99, 1000), partial.Content.Headers.ContentRange);
        Assert.Equal(100, partial.Content.Headers.ContentLength);
        Assert.Equal("video/mp4", partial.Content.Headers.ContentType?.MediaType);
        Assert.Contains("bytes", partial.Headers.AcceptRanges);
        Assert.True(partial.Headers.CacheControl?.Private);
        Assert.Equal(bytes[..100], await partial.Content.ReadAsByteArrayAsync());

        // Del byte 900 hasta el final.
        using var last = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{movie.Id}/stream");
        last.Headers.Range = new RangeHeaderValue(900, null);
        using var tail = await admin.SendAsync(last);

        Assert.Equal(HttpStatusCode.PartialContent, tail.StatusCode);
        Assert.Equal(new ContentRangeHeaderValue(900, 999, 1000), tail.Content.Headers.ContentRange);
        Assert.Equal(bytes[900..], await tail.Content.ReadAsByteArrayAsync());

        // Sin Range, el archivo completo.
        using var whole = await admin.GetAsync($"/api/items/{movie.Id}/stream");

        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal(1000, whole.Content.Headers.ContentLength);
        Assert.Contains("bytes", whole.Headers.AcceptRanges);
        Assert.Equal(bytes, await whole.Content.ReadAsByteArrayAsync());

        // Un rango fuera del archivo no se puede satisfacer.
        using var beyond = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{movie.Id}/stream");
        beyond.Headers.Range = new RangeHeaderValue(5000, null);
        using var unsatisfiable = await admin.SendAsync(beyond);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, unsatisfiable.StatusCode);
    }

    [Fact]
    public async Task El_stream_usa_el_tipo_MIME_de_cada_extension()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var tipos = new Dictionary<string, string>
        {
            ["mp3"] = "audio/mpeg",
            ["m4a"] = "audio/mp4",
            ["aac"] = "audio/aac",
            ["flac"] = "audio/flac",
            ["ogg"] = "audio/ogg",
            ["opus"] = "audio/ogg",
            ["wav"] = "audio/wav",
        };
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Musica",
            [.. tipos.Keys.Select((extension, index) => $"Artista/Album (2020)/{index + 1:00} - {extension}.{extension}")]);
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Música"), LibraryType.Music, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        var album = Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items);
        var tracks = (await ChildrenAsync(admin, album.Id))!;

        Assert.Equal(tipos.Count, tracks.Count);
        foreach (var track in tracks)
        {
            // El título de cada pista es su extensión.
            using var response = await admin.GetAsync($"/api/items/{track.Id}/stream");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(tipos[track.Title], response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Lo_que_no_se_reproduce_y_lo_que_esta_fuera_de_las_carpetas_de_medios_responde_404()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin);
        var episode = anime.LatEpisodes[2];

        // Una serie, una carpeta y algo que no existe no se reproducen.
        foreach (var id in new[] { anime.SeriesId, anime.Lat.Id, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{id}/stream")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{id}/playback")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PutProgressAsync(admin, id, 10)).StatusCode);
        }

        // Un episodio que ya no está en el disco.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/items/{episode.Id}/stream")).StatusCode);
        File.Delete(Path.Combine(anime.Folder, "Mi Anime", "Lat", "03.mp4"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{episode.Id}/stream")).StatusCode);

        // Una ruta fuera de las carpetas de medios nunca se sirve, aunque el archivo exista.
        var outside = Path.Combine(Path.GetTempPath(), $"fuera-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(outside, [1, 2, 3]);
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
                var other = await db.MediaItems.OfType<PlayableItem>().SingleAsync(i => i.Id == anime.LatEpisodes[1].Id);
                other.FilePath = outside;
                await db.SaveChangesAsync();
            }

            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{anime.LatEpisodes[1].Id}/stream")).StatusCode);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Un_usuario_sin_acceso_recibe_404_en_todo_lo_de_reproduccion_y_sin_sesion_recibe_401()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin);
        var episode = anime.LatEpisodes[0];
        const string outsider = "sin-acceso";
        await TestUsers.EnsureUserAsync(factory, outsider);
        using var user = await TestUsers.LoginAsync(factory, outsider);

        foreach (var url in new[]
        {
            $"/api/items/{episode.Id}/stream",
            $"/api/items/{episode.Id}/playback",
            $"/api/items/{anime.SeriesId}/next-up",
            $"/api/items/{anime.Lat.Id}/next-up",
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync(url)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await PutProgressAsync(user, episode.Id, 10)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsync($"/api/items/{episode.Id}/played", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsync($"/api/items/{anime.SeriesId}/played", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.DeleteAsync($"/api/items/{episode.Id}/played")).StatusCode);

        // Lo que intentó no dejó nada: el administrador sigue sin nada visto.
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!;
        Assert.False(detail.Played);
        Assert.Equal(0, detail.PositionSeconds);

        using var anonymous = factory.CreateClient();
        foreach (var url in new[]
        {
            $"/api/items/{episode.Id}/stream",
            $"/api/items/{episode.Id}/playback",
            $"/api/items/{anime.SeriesId}/next-up",
            "/api/me/continue-watching",
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task El_progreso_se_guarda_y_pasa_a_visto_despues_del_90_por_ciento()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin);
        var episode = anime.LatEpisodes[0];

        Assert.Equal(HttpStatusCode.NoContent, (await PutProgressAsync(admin, episode.Id, 30)).StatusCode);

        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!;
        Assert.Equal(30, detail.PositionSeconds);
        Assert.False(detail.Played);
        var listed = (await ChildrenAsync(admin, anime.Lat.Id))!.Single(c => c.Id == episode.Id);
        Assert.Equal(30, listed.PositionSeconds);
        Assert.False(listed.Played);
        Assert.Equal(30, (await admin.GetJsonAsync<PlaybackInfo>($"/api/items/{episode.Id}/playback"))!.PositionSeconds);

        // Un segundo guardado actualiza la misma fila.
        Assert.Equal(HttpStatusCode.NoContent, (await PutProgressAsync(admin, episode.Id, 45.5)).StatusCode);
        Assert.Equal(45.5, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!.PositionSeconds);

        // Hasta el 90 % todavía no está visto; después, sí, y la posición vuelve a 0.
        await PutProgressAsync(admin, episode.Id, 89);
        Assert.False((await admin.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!.Played);
        await PutProgressAsync(admin, episode.Id, 95);
        detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!;
        Assert.True(detail.Played);
        Assert.Equal(0, detail.PositionSeconds);

        // Volver a verlo a medias no le quita lo visto.
        await PutProgressAsync(admin, episode.Id, 20);
        detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!;
        Assert.True(detail.Played);
        Assert.Equal(20, detail.PositionSeconds);
    }

    [Fact]
    public async Task El_progreso_rechaza_valores_negativos_que_no_son_numeros_o_que_faltan()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin);
        var url = $"/api/items/{anime.LatEpisodes[0].Id}/progress";

        Assert.Equal(HttpStatusCode.BadRequest, (await PutProgressAsync(admin, anime.LatEpisodes[0].Id, -1)).StatusCode);
        foreach (var body in new[] { "{}", "{\"positionSeconds\":null}", "{\"positionSeconds\":\"NaN\"}", "{\"positionSeconds\":\"abc\"}" })
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync(url, content)).StatusCode);
        }

        // Nada de eso se guardó.
        Assert.Equal(0, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{anime.LatEpisodes[0].Id}"))!.PositionSeconds);
    }

    [Fact]
    public async Task Sin_duracion_conocida_el_progreso_nunca_marca_como_visto()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin, duration: null);
        var episode = anime.LatEpisodes[0];

        await PutProgressAsync(admin, episode.Id, 5000);

        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!;
        Assert.False(detail.Played);
        Assert.Equal(5000, detail.PositionSeconds);
    }

    [Fact]
    public async Task El_progreso_es_de_cada_usuario()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin);
        var episode = anime.LatEpisodes[0];
        using var user = await LoginNewUserAsync(anime.LibraryId);

        await PutProgressAsync(admin, episode.Id, 40);
        await PutProgressAsync(user, episode.Id, 10);

        Assert.Equal(40, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!.PositionSeconds);
        Assert.Equal(10, (await user.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!.PositionSeconds);
    }

    [Fact]
    public async Task Marcar_una_temporada_como_vista_y_como_no_vista_afecta_a_todos_sus_episodios()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin);

        // Todo sin ver: la serie y las carpetas dicen cuántos episodios faltan.
        Assert.Equal(5, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{anime.SeriesId}"))!.UnplayedCount);
        var groups = (await ChildrenAsync(admin, anime.SeriesId))!;
        Assert.Equal(new int?[] { 3, 2 }, groups.Select(g => g.UnplayedCount).ToArray());

        // Un episodio a medias (con fila) y los demás sin fila: las dos rutas del marcado en lote.
        await PutProgressAsync(admin, anime.LatEpisodes[0].Id, 10);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/items/{anime.Lat.Id}/played", null)).StatusCode);

        var latEpisodes = (await ChildrenAsync(admin, anime.Lat.Id))!;
        Assert.All(latEpisodes, e => Assert.True(e.Played));
        Assert.All(latEpisodes, e => Assert.Equal(0, e.PositionSeconds));
        Assert.All((await ChildrenAsync(admin, anime.Sub.Id))!, e => Assert.False(e.Played));
        groups = (await ChildrenAsync(admin, anime.SeriesId))!;
        Assert.Equal(new int?[] { 0, 2 }, groups.Select(g => g.UnplayedCount).ToArray());
        Assert.Equal(2, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{anime.SeriesId}"))!.UnplayedCount);
        Assert.Equal(0, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{anime.Lat.Id}"))!.UnplayedCount);

        // Repetirlo no cambia nada ni falla.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/items/{anime.Lat.Id}/played", null)).StatusCode);
        Assert.All((await ChildrenAsync(admin, anime.Lat.Id))!, e => Assert.True(e.Played));

        // No visto: vuelven todos.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/items/{anime.Lat.Id}/played")).StatusCode);

        latEpisodes = (await ChildrenAsync(admin, anime.Lat.Id))!;
        Assert.All(latEpisodes, e => Assert.False(e.Played));
        Assert.Equal(3, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{anime.Lat.Id}"))!.UnplayedCount);

        // Una serie completa, de un solo golpe, y un episodio suelto.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/items/{anime.SeriesId}/played", null)).StatusCode);
        Assert.Equal(0, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{anime.SeriesId}"))!.UnplayedCount);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/items/{anime.SeriesId}/played")).StatusCode);
        Assert.Equal(5, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{anime.SeriesId}"))!.UnplayedCount);

        var episode = anime.SubEpisodes[1];
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/items/{episode.Id}/played", null)).StatusCode);
        Assert.True((await admin.GetJsonAsync<ItemDetail>($"/api/items/{episode.Id}"))!.Played);
        Assert.Equal(4, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{anime.SeriesId}"))!.UnplayedCount);
    }

    [Fact]
    public async Task Continuar_viendo_lista_lo_que_esta_a_medias_del_mas_reciente_al_mas_antiguo()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin);
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas", "Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4");
        var movies = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Películas"), LibraryType.Movies, folder);
        await LibraryTestSupport.ScanAsync(admin, movies.Id);
        await SetDurationsAsync(movies.Id, TestDuration);
        var movie = Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{movies.Id}/items"))!.Items);
        using var user = await LoginNewUserAsync(anime.LibraryId, movies.Id);

        Assert.Empty((await user.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!);

        // Primero la película, después un episodio con miniatura propia y uno sin ella.
        await PutProgressAsync(user, movie.Id, 20);
        await Task.Delay(20);
        await PutProgressAsync(user, anime.LatEpisodes[0].Id, 35);
        await Task.Delay(20);
        await PutProgressAsync(user, anime.LatEpisodes[1].Id, 50);

        var row = (await user.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!;
        Assert.Equal(new[] { anime.LatEpisodes[1].Id, anime.LatEpisodes[0].Id, movie.Id }, row.Select(r => r.Id).ToArray());
        var episode = row[0];
        Assert.Equal("Episode", episode.Kind);
        Assert.Equal("Episodio 2", episode.Title);
        Assert.Equal("Mi Anime", episode.SeriesTitle);
        Assert.Equal("Lat", episode.ParentTitle);
        Assert.Equal(50, episode.PositionSeconds);
        Assert.Equal(TestDuration, episode.DurationSeconds);
        Assert.NotNull(episode.LastPlayedAt);
        // Sin miniatura propia usa la de su carpeta (el póster de la serie); con ella, la suya.
        Assert.Equal(anime.Lat.Id, episode.ImageId);
        Assert.Equal(anime.LatEpisodes[0].Id, row[1].ImageId);
        Assert.Equal("Movie", row[2].Kind);
        Assert.Null(row[2].SeriesTitle);
        Assert.Null(row[2].ParentTitle);
        Assert.Equal(1, (await user.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching?limit=1"))!.Count);

        // Lo que ya se vio, o no tiene posición, sale de la fila.
        await PutProgressAsync(user, movie.Id, 95);
        await PutProgressAsync(user, anime.LatEpisodes[0].Id, 0);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsync($"/api/items/{anime.LatEpisodes[1].Id}/played", null)).StatusCode);
        Assert.Empty((await user.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!);

        // La fila es de cada usuario y respeta los permisos: ni el administrador ni quien no tiene acceso la ven.
        using var outsider = await LoginNewUserAsync();
        await PutProgressAsync(user, anime.SubEpisodes[0].Id, 10);
        Assert.Empty((await outsider.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!);
        Assert.DoesNotContain(
            (await admin.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!,
            r => r.Id == anime.SubEpisodes[0].Id);
    }

    [Fact]
    public async Task A_continuacion_sigue_dentro_de_la_misma_carpeta_sin_mezclar_Lat_y_Sub()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin);
        using var user = await LoginNewUserAsync(anime.LibraryId);

        // Sin historial: el primero de la primera carpeta; y cada carpeta, su primero.
        var first = (await NextUpAsync(user, anime.SeriesId))!;
        Assert.Equal(anime.LatEpisodes[0].Id, first.Id);
        Assert.Equal("Mi Anime", first.SeriesTitle);
        Assert.Equal("Lat", first.ParentTitle);
        Assert.Equal(anime.LatEpisodes[0].Id, first.ImageId);
        Assert.Equal(anime.SubEpisodes[0].Id, (await NextUpAsync(user, anime.Sub.Id))!.Id);

        // Vio el 1 de Lat: sigue el 2 de Lat, en la serie y en la carpeta.
        await PutProgressAsync(user, anime.LatEpisodes[0].Id, 95);
        Assert.Equal(anime.LatEpisodes[1].Id, (await NextUpAsync(user, anime.Lat.Id))!.Id);
        Assert.Equal(anime.LatEpisodes[1].Id, (await NextUpAsync(user, anime.SeriesId))!.Id);

        // Vio después el 1 de Sub: la serie sigue por Sub, y cada carpeta por lo suyo.
        await Task.Delay(20);
        await PutProgressAsync(user, anime.SubEpisodes[0].Id, 95);
        Assert.Equal(anime.SubEpisodes[1].Id, (await NextUpAsync(user, anime.SeriesId))!.Id);
        Assert.Equal(anime.SubEpisodes[1].Id, (await NextUpAsync(user, anime.Sub.Id))!.Id);
        Assert.Equal(anime.LatEpisodes[1].Id, (await NextUpAsync(user, anime.Lat.Id))!.Id);

        // Dejó a medias el 2 de Lat: ese mismo es lo que sigue, con su posición.
        await Task.Delay(20);
        await PutProgressAsync(user, anime.LatEpisodes[1].Id, 40);
        var resume = (await NextUpAsync(user, anime.SeriesId))!;
        Assert.Equal(anime.LatEpisodes[1].Id, resume.Id);
        Assert.Equal(40, resume.PositionSeconds);

        // Terminó Sub: no pasa a Lat, aunque a Lat todavía le falte. Lat sigue donde iba.
        await Task.Delay(20);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsync($"/api/items/{anime.Sub.Id}/played", null)).StatusCode);
        Assert.Null(await NextUpAsync(user, anime.Sub.Id));
        Assert.Null(await NextUpAsync(user, anime.SeriesId));
        Assert.Equal(anime.LatEpisodes[1].Id, (await NextUpAsync(user, anime.Lat.Id))!.Id);

        // Todo visto: no queda nada. Al marcarlo como no visto, vuelve a empezar.
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsync($"/api/items/{anime.SeriesId}/played", null)).StatusCode);
        Assert.Null(await NextUpAsync(user, anime.Lat.Id));
        Assert.Null(await NextUpAsync(user, anime.SeriesId));
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync($"/api/items/{anime.SeriesId}/played")).StatusCode);
        Assert.Equal(anime.LatEpisodes[0].Id, (await NextUpAsync(user, anime.SeriesId))!.Id);

        // Solo las series y las carpetas tienen "A continuación".
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/items/{anime.LatEpisodes[0].Id}/next-up")).StatusCode);
    }

    [Fact]
    public async Task A_continuacion_pasa_a_la_siguiente_temporada_con_numero_pero_no_entre_carpetas_sin_numero()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Series",
            "Mi Serie (2019)/Temporada 01/Mi Serie S01E01.mkv",
            "Mi Serie (2019)/Temporada 01/Mi Serie S01E02.mkv",
            "Mi Serie (2019)/Temporada 02/Mi Serie S02E01.mkv");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Series"), LibraryType.Series, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        await SetDurationsAsync(library.Id, TestDuration);
        var series = Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items);
        var seasons = (await ChildrenAsync(admin, series.Id))!;
        var seasonOne = (await ChildrenAsync(admin, seasons[0].Id))!;
        var seasonTwo = (await ChildrenAsync(admin, seasons[1].Id))!;
        using var user = await LoginNewUserAsync(library.Id);

        await PutProgressAsync(user, seasonOne[0].Id, 95);
        Assert.Equal(seasonOne[1].Id, (await NextUpAsync(user, series.Id))!.Id);

        // Al terminar la temporada 1, la serie sigue con la 2 (la temporada 1, por sí sola, ya no tiene nada).
        await PutProgressAsync(user, seasonOne[1].Id, 95);
        Assert.Equal(seasonTwo[0].Id, (await NextUpAsync(user, series.Id))!.Id);
        Assert.Null(await NextUpAsync(user, seasons[0].Id));
        Assert.Equal(seasonTwo[0].Id, (await NextUpAsync(user, seasons[1].Id))!.Id);
    }

    [Fact]
    public async Task El_reproductor_recibe_el_contexto_y_el_anterior_y_siguiente_de_la_misma_carpeta()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var anime = await CreateAnimeAsync(admin);

        var middle = (await admin.GetJsonAsync<PlaybackInfo>($"/api/items/{anime.LatEpisodes[1].Id}/playback"))!;
        Assert.Equal(anime.LatEpisodes[1].Id, middle.Id);
        Assert.Equal("Episode", middle.Kind);
        Assert.Equal("Episodio 2", middle.Title);
        Assert.Equal("Mi Anime", middle.SeriesTitle);
        Assert.Equal("Lat", middle.ParentTitle);
        Assert.Equal(anime.Lat.Id, middle.ParentId);
        Assert.Equal($"/api/items/{middle.Id}/stream", middle.StreamUrl);
        Assert.Equal("video/mp4", middle.ContentType);
        Assert.True(middle.IsVideo);
        Assert.Equal(TestDuration, middle.DurationSeconds);
        Assert.Equal(0, middle.PositionSeconds);
        Assert.Equal(anime.LatEpisodes[0].Id, middle.PreviousId);
        Assert.Equal(anime.LatEpisodes[2].Id, middle.NextId);
        Assert.Equal("Episodio 3", middle.NextTitle);

        // Los extremos de cada carpeta no saltan a la otra: el 3 de Lat es el último, el 1 de Sub el primero.
        var lastLat = (await admin.GetJsonAsync<PlaybackInfo>($"/api/items/{anime.LatEpisodes[2].Id}/playback"))!;
        Assert.Equal(anime.LatEpisodes[1].Id, lastLat.PreviousId);
        Assert.Null(lastLat.NextId);
        Assert.Null(lastLat.NextTitle);
        var firstSub = (await admin.GetJsonAsync<PlaybackInfo>($"/api/items/{anime.SubEpisodes[0].Id}/playback"))!;
        Assert.Null(firstSub.PreviousId);
        Assert.Equal(anime.SubEpisodes[1].Id, firstSub.NextId);
        Assert.Equal("Sub", firstSub.ParentTitle);

        // Las respuestas nunca traen la ruta del archivo.
        var json = await admin.GetStringAsync($"/api/items/{middle.Id}/playback");
        Assert.DoesNotContain("filePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFileName(factory.MediaPath), json);
    }

    [Fact]
    public async Task Los_episodios_sin_numero_y_las_peliculas_no_tienen_anterior_ni_siguiente()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Anime",
            // Una película con una versión por idioma: dos archivos sin número, que no son una secuencia.
            "Mi Pelicula/Lat.mp4",
            "Mi Pelicula/Sub.mp4",
            // Episodios directo en la serie, sin carpetas.
            "Mi Anime/[Grupo] Mi Anime - 05 [1080p].mkv",
            "Mi Anime/[Grupo] Mi Anime - 06 [1080p].mkv");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Anime"), LibraryType.Anime, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        await SetDurationsAsync(library.Id, TestDuration);
        var items = (await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items;
        var film = items.Single(i => i.Title == "Mi Pelicula");
        var series = items.Single(i => i.Title == "Mi Anime");

        var versions = (await ChildrenAsync(admin, film.Id))!;
        Assert.Equal(new[] { "Lat", "Sub" }, versions.Select(v => v.Title).ToArray());
        foreach (var version in versions)
        {
            var info = (await admin.GetJsonAsync<PlaybackInfo>($"/api/items/{version.Id}/playback"))!;
            Assert.Null(info.PreviousId);
            Assert.Null(info.NextId);
            Assert.Equal("Mi Pelicula", info.SeriesTitle);
            Assert.Null(info.ParentTitle);
        }

        // Viste "Lat" de la película: no se sugiere "Sub" como lo que sigue.
        using var user = await LoginNewUserAsync(library.Id);
        Assert.Equal(versions[0].Id, (await NextUpAsync(user, film.Id))!.Id);
        await PutProgressAsync(user, versions[0].Id, 95);
        Assert.Null(await NextUpAsync(user, film.Id));

        // Episodios con número directo en la serie: sin carpeta de por medio, se siguen en orden (mkv).
        var episodes = (await ChildrenAsync(admin, series.Id))!;
        var five = (await admin.GetJsonAsync<PlaybackInfo>($"/api/items/{episodes[0].Id}/playback"))!;
        Assert.Null(five.ParentTitle);
        Assert.Equal("video/x-matroska", five.ContentType);
        Assert.Null(five.PreviousId);
        Assert.Equal(episodes[1].Id, five.NextId);
    }

    [Fact]
    public async Task Las_pistas_de_un_album_se_siguen_en_orden_y_se_marcan_como_vistas()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Musica",
            "Artista/Album (2020)/01 - Primera.flac",
            "Artista/Album (2020)/02 - Segunda.flac");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Música"), LibraryType.Music, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        var album = Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items);
        var tracks = (await ChildrenAsync(admin, album.Id))!;

        var first = (await admin.GetJsonAsync<PlaybackInfo>($"/api/items/{tracks[0].Id}/playback"))!;
        Assert.False(first.IsVideo);
        Assert.Equal("audio/flac", first.ContentType);
        Assert.Equal("Artista", first.Artist);
        Assert.Equal("Album", first.ParentTitle);
        Assert.Null(first.SeriesTitle);
        Assert.Equal(tracks[1].Id, first.NextId);

        // Una pista reproducida hasta el final se marca como vista, y el álbum con ella.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/items/{tracks[0].Id}/played", null)).StatusCode);
        Assert.True((await ChildrenAsync(admin, album.Id))!.First(t => t.Id == tracks[0].Id).Played);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/items/{album.Id}/played", null)).StatusCode);
        Assert.All((await ChildrenAsync(admin, album.Id))!, t => Assert.True(t.Played));

        // Las pistas no son video: no entran en "Continuar viendo".
        await PutProgressAsync(admin, tracks[1].Id, 30);
        Assert.DoesNotContain(
            (await admin.GetJsonAsync<List<ResumeItem>>("/api/me/continue-watching"))!,
            r => r.Id == tracks[1].Id);
    }
}
