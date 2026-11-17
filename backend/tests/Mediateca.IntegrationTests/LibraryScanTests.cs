using System.Net;
using Mediateca.Api.Items;
using Mediateca.Api.Libraries;
using Mediateca.Core.Libraries;
using Mediateca.Core.Scanning;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Escaneo de bibliotecas con árboles de archivos vacíos (PLAN.md, sección 7) y las consultas que
/// usa Angular: cuadrícula, filtros, detalle, hijos, imagen y "Agregado recientemente".
/// </summary>
public sealed class LibraryScanTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => TestUsers.EnsureAdminAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<ItemPage?> GetItemsAsync(HttpClient client, Guid libraryId, string query = "") =>
        client.GetJsonAsync<ItemPage>($"/api/libraries/{libraryId}/items{query}");

    [Fact]
    public async Task Escanea_peliculas_con_busqueda_filtros_poster_y_agregado_recientemente()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Peliculas",
            "Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4",
            "Otra Pelicula (2010)/Otra Pelicula (2010).mkv",
            "Otra Pelicula (2010)/notas.txt");
        // Póster junto a la película y un archivo oculto que no debe contar.
        LibraryTestSupport.WriteFile(folder, "Otra Pelicula (2010)/poster.jpg", [1, 2, 3, 4]);
        LibraryTestSupport.WriteFile(folder, ".oculta/Escondida (2001)/Escondida (2001).mp4", []);
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Películas"), LibraryType.Movies, folder);

        var scan = await LibraryTestSupport.ScanAsync(admin, library.Id);

        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Equal(2, scan.Added);
        Assert.Equal(0, scan.Errors);
        Assert.NotNull(scan.StartedAt);
        Assert.NotNull(scan.FinishedAt);

        var page = (await GetItemsAsync(admin, library.Id))!;
        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { "Big Buck Bunny", "Otra Pelicula" }, page.Items.Select(i => i.Title).ToArray());
        var bunny = page.Items[0];
        Assert.Equal("Movie", bunny.Kind);
        Assert.Equal(2008, bunny.Year);
        Assert.False(bunny.HasImage);
        var other = page.Items[1];
        Assert.True(other.HasImage);

        // La biblioteca ya cuenta sus elementos y guarda la fecha del escaneo.
        var libraries = (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!;
        var listed = libraries.Single(l => l.Id == library.Id);
        Assert.Equal(2, listed.ItemCount);
        Assert.NotNull(listed.LastScannedAt);
        Assert.Equal(ScanStatus.Completed, listed.LastScan?.Status);

        // Búsqueda, año, orden y paginación.
        Assert.Equal(1, (await GetItemsAsync(admin, library.Id, "?search=buck"))!.Total);
        Assert.Equal(0, (await GetItemsAsync(admin, library.Id, "?search=zzz"))!.Total);
        Assert.Equal(0, (await GetItemsAsync(admin, library.Id, "?search=%25"))!.Total);
        Assert.Equal(1, (await GetItemsAsync(admin, library.Id, "?year=2010"))!.Total);
        Assert.Equal(0, (await GetItemsAsync(admin, library.Id, "?genre=Drama"))!.Total);
        var byYear = (await GetItemsAsync(admin, library.Id, "?sort=year"))!;
        Assert.Equal(new[] { "Otra Pelicula", "Big Buck Bunny" }, byYear.Items.Select(i => i.Title).ToArray());
        var secondPage = (await GetItemsAsync(admin, library.Id, "?pageSize=1&page=2"))!;
        Assert.Equal(2, secondPage.Total);
        Assert.Equal("Otra Pelicula", Assert.Single(secondPage.Items).Title);
        Assert.Equal(1, secondPage.PageSize);

        var filters = (await admin.GetJsonAsync<LibraryFiltersResponse>($"/api/libraries/{library.Id}/filters"))!;
        Assert.Equal(new[] { 2010, 2008 }, filters.Years.ToArray());
        Assert.Empty(filters.Genres);

        // Detalle: sin la ruta del archivo en el servidor.
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{bunny.Id}"))!;
        Assert.Equal("Movie", detail.Kind);
        Assert.Equal(library.Name, detail.LibraryName);
        Assert.Equal(LibraryType.Movies, detail.LibraryType);
        Assert.Equal(0, detail.FileSize);
        Assert.Empty(detail.Ancestors);
        Assert.Null(detail.ParentId);
        var json = await admin.GetStringAsync($"/api/items/{bunny.Id}");
        Assert.DoesNotContain(Path.GetFileName(Path.GetDirectoryName(folder))!, json);
        Assert.DoesNotContain("filePath", json, StringComparison.OrdinalIgnoreCase);

        // Imagen: la película con póster la sirve con su tipo; la otra responde 404.
        var image = await admin.GetAsync($"/api/items/{other.Id}/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await image.Content.ReadAsByteArrayAsync());
        Assert.Contains("max-age", image.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{bunny.Id}/image")).StatusCode);

        var recent = (await admin.GetJsonAsync<List<ItemSummary>>("/api/me/recently-added?limit=50"))!;
        Assert.Contains(recent, i => i.Id == bunny.Id);
        Assert.Single((await admin.GetJsonAsync<List<ItemSummary>>("/api/me/recently-added?limit=1"))!);
    }

    [Fact]
    public async Task Escanea_series_con_temporadas_y_episodios_en_orden()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Series",
            "Mi Serie (2019)/Temporada 01/Mi Serie S01E02.mkv",
            "Mi Serie (2019)/Temporada 01/Mi Serie S01E01.mkv",
            "Mi Serie (2019)/Temporada 02/Mi Serie S02E01.mkv",
            "Mi Serie (2019)/Especiales/Mi Serie S00E01.mkv");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Series"), LibraryType.Series, folder);

        var scan = await LibraryTestSupport.ScanAsync(admin, library.Id);

        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Equal(4, scan.Added);

        // La cuadrícula solo trae el primer nivel: la serie, no sus temporadas ni episodios.
        var page = (await GetItemsAsync(admin, library.Id))!;
        var series = Assert.Single(page.Items);
        Assert.Equal("Series", series.Kind);
        Assert.Equal("Mi Serie", series.Title);
        Assert.Equal(2019, series.Year);

        // Temporadas por número: Especiales (0), 1 y 2.
        var seasons = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{series.Id}/children"))!;
        Assert.Equal(new int?[] { 0, 1, 2 }, seasons.Select(s => s.SeasonNumber).ToArray());
        Assert.All(seasons, s => Assert.Equal("Season", s.Kind));
        Assert.Equal(new[] { "Especiales", "Temporada 1", "Temporada 2" }, seasons.Select(s => s.Title).ToArray());

        var seasonOne = seasons[1];
        var episodes = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{seasonOne.Id}/children"))!;
        Assert.Equal(new int?[] { 1, 2 }, episodes.Select(e => e.EpisodeNumber).ToArray());
        Assert.All(episodes, e => Assert.Equal("Episode", e.Kind));
        Assert.Equal("Episodio 2", episodes[1].Title);

        // Un episodio sabe a qué serie y temporada pertenece, para la ruta de navegación.
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episodes[1].Id}"))!;
        Assert.Equal(new[] { "Series", "Season" }, detail.Ancestors.Select(a => a.Kind).ToArray());
        Assert.Equal(series.Id, detail.Ancestors[0].Id);
        Assert.Equal(seasonOne.Id, detail.ParentId);
        Assert.Equal("Temporada 1", detail.ParentTitle);
        Assert.Equal(1, detail.SeasonNumber);
        Assert.Equal(2, detail.EpisodeNumber);
    }

    [Fact]
    public async Task Escanea_anime_con_episodios_de_numero_absoluto_dentro_de_la_serie()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Anime",
            "Mi Anime/[Grupo] Mi Anime - 06 [1080p].mkv",
            "Mi Anime/[Grupo] Mi Anime - 05 [1080p].mkv");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Anime"), LibraryType.Anime, folder);

        var scan = await LibraryTestSupport.ScanAsync(admin, library.Id);

        Assert.Equal(2, scan.Added);
        var series = Assert.Single((await GetItemsAsync(admin, library.Id))!.Items);
        Assert.Equal("Mi Anime", series.Title);

        // Sin temporadas: los episodios cuelgan directo de la serie.
        var episodes = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{series.Id}/children"))!;
        Assert.Equal(new int?[] { 5, 6 }, episodes.Select(e => e.AbsoluteNumber).ToArray());
        Assert.All(episodes, e => Assert.Equal("Episode", e.Kind));
        Assert.All(episodes, e => Assert.Null(e.SeasonNumber));
        Assert.Equal("Episodio 5", episodes[0].Title);

        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episodes[0].Id}"))!;
        Assert.Equal(series.Id, Assert.Single(detail.Ancestors).Id);
    }

    [Fact]
    public async Task Escanea_musica_con_los_datos_de_la_ruta_cuando_el_archivo_no_tiene_etiquetas()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Musica",
            "Artista/Album (2020)/02 - Otra.flac",
            "Artista/Album (2020)/01 - Cancion.flac");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Música"), LibraryType.Music, folder);

        var scan = await LibraryTestSupport.ScanAsync(admin, library.Id);

        // Los archivos vacíos no se pueden leer: no es un error, se usa la ruta.
        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Equal(2, scan.Added);
        Assert.Equal(0, scan.Errors);

        var album = Assert.Single((await GetItemsAsync(admin, library.Id))!.Items);
        Assert.Equal("MusicAlbum", album.Kind);
        Assert.Equal("Album", album.Title);
        Assert.Equal(2020, album.Year);
        Assert.Equal("Artista", album.Artist);

        var tracks = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{album.Id}/children"))!;
        Assert.Equal(new int?[] { 1, 2 }, tracks.Select(t => t.TrackNumber).ToArray());
        Assert.Equal(new[] { "Cancion", "Otra" }, tracks.Select(t => t.Title).ToArray());
        Assert.All(tracks, t => Assert.Equal("Track", t.Kind));

        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{tracks[0].Id}"))!;
        Assert.Equal("Album", detail.ParentTitle);
        Assert.Equal(1, detail.TrackNumber);
    }

    [Fact]
    public async Task Un_segundo_escaneo_sin_cambios_no_agrega_ni_modifica_nada()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Series",
            "Mi Serie (2019)/Temporada 01/Mi Serie S01E01.mkv",
            "Mi Serie (2019)/Temporada 01/Mi Serie S01E02.mkv");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Series"), LibraryType.Series, folder);
        var first = await LibraryTestSupport.ScanAsync(admin, library.Id);
        Assert.Equal(2, first.Added);

        var second = await LibraryTestSupport.ScanAsync(admin, library.Id);

        Assert.Equal(ScanStatus.Completed, second.Status);
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(0, second.Removed);
        Assert.Equal(1, (await GetItemsAsync(admin, library.Id))!.Total);
    }

    [Fact]
    public async Task Detecta_archivos_nuevos_cambiados_y_eliminados()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory,
            "Series",
            "Mi Serie (2019)/Temporada 01/Mi Serie S01E01.mkv",
            "Mi Serie (2019)/Temporada 01/Mi Serie S01E02.mkv");
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Series"), LibraryType.Series, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        var series = Assert.Single((await GetItemsAsync(admin, library.Id))!.Items);

        // Un archivo nuevo en la misma serie, y uno que cambia de tamaño.
        LibraryTestSupport.WriteFile(folder, "Mi Serie (2019)/Temporada 01/Mi Serie S01E03.mkv", []);
        LibraryTestSupport.WriteFile(folder, "Mi Serie (2019)/Temporada 01/Mi Serie S01E01.mkv", [1, 2, 3]);
        var changed = await LibraryTestSupport.ScanAsync(admin, library.Id);

        Assert.Equal(1, changed.Added);
        Assert.Equal(1, changed.Updated);
        Assert.Equal(0, changed.Removed);
        // La serie y la temporada se reutilizan: no se duplican.
        Assert.Equal(series.Id, Assert.Single((await GetItemsAsync(admin, library.Id))!.Items).Id);
        var seasons = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{series.Id}/children"))!;
        var episodes = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{Assert.Single(seasons).Id}/children"))!;
        Assert.Equal(new int?[] { 1, 2, 3 }, episodes.Select(e => e.EpisodeNumber).ToArray());
        var updated = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{episodes[0].Id}"))!;
        Assert.Equal(3, updated.FileSize);

        // Se borra un archivo: su episodio desaparece.
        File.Delete(Path.Combine(folder, "Mi Serie (2019)", "Temporada 01", "Mi Serie S01E02.mkv"));
        var removed = await LibraryTestSupport.ScanAsync(admin, library.Id);

        Assert.Equal(1, removed.Removed);
        Assert.Equal(0, removed.Added);
        episodes = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{seasons[0].Id}/children"))!;
        Assert.Equal(new int?[] { 1, 3 }, episodes.Select(e => e.EpisodeNumber).ToArray());

        // Sin archivos, también se van la temporada y la serie, que quedaron vacías.
        Directory.Delete(Path.Combine(folder, "Mi Serie (2019)"), recursive: true);
        var empty = await LibraryTestSupport.ScanAsync(admin, library.Id);

        Assert.Equal(2, empty.Removed);
        Assert.Equal(0, (await GetItemsAsync(admin, library.Id))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{series.Id}")).StatusCode);
    }

    [Fact]
    public async Task Pedir_un_escaneo_mientras_hay_otro_activo_devuelve_el_mismo()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var files = Enumerable.Range(1, 30).Select(n => $"Peli {n} ({2000 + n})/Peli {n} ({2000 + n}).mkv").ToArray();
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas", files);
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Muchas"), LibraryType.Movies, folder);

        var first = await admin.PostAsync($"/api/admin/libraries/{library.Id}/scan", null);
        var second = await admin.PostAsync($"/api/admin/libraries/{library.Id}/scan", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var firstJob = (await first.ReadJsonAsync<ScanJobResponse>())!;
        var secondJob = (await second.ReadJsonAsync<ScanJobResponse>())!;

        // Si el primero ya había terminado, el segundo es uno nuevo; si no, es el mismo. Nunca dos activos.
        if (secondJob.Id != firstJob.Id)
        {
            Assert.Equal(ScanStatus.Completed, (await admin.GetJsonAsync<ScanJobResponse>($"/api/admin/scans/{firstJob.Id}"))!.Status);
        }

        var done = await LibraryTestSupport.ScanAsync(admin, library.Id);
        Assert.Equal(ScanStatus.Completed, done.Status);
        Assert.Equal(30, (await GetItemsAsync(admin, library.Id))!.Total);
    }

    [Fact]
    public async Task Varias_solicitudes_simultaneas_del_mismo_escaneo_devuelven_el_mismo_escaneo()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var files = Enumerable.Range(1, 60).Select(n => $"Peli {n} ({1900 + n})/Peli {n} ({1900 + n}).mkv").ToArray();
        var folder = LibraryTestSupport.CreateFolder(factory, "Peliculas", files);
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Simultaneas"), LibraryType.Movies, folder);

        // El escaneo queda en la cola hasta que el procesador lo toma; todas las solicitudes llegan antes.
        // El índice único de la base de datos deja crear solo uno y las demás devuelven ese mismo.
        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => admin.PostAsync($"/api/admin/libraries/{library.Id}/scan", null)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        var jobs = await Task.WhenAll(responses.Select(response => response.ReadJsonAsync<ScanJobResponse>()));
        Assert.Single(jobs.Select(job => job!.Id).Distinct());

        var done = await LibraryTestSupport.ScanAsync(admin, library.Id);
        Assert.Equal(ScanStatus.Completed, done.Status);
        Assert.Equal(60, (await GetItemsAsync(admin, library.Id))!.Total);
    }

    [Fact]
    public async Task Un_escaneo_que_no_existe_responde_404()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/scans/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/libraries/{Guid.NewGuid()}/scan", null)).StatusCode);
    }
}
