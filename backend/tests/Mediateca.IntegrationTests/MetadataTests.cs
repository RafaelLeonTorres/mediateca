using System.Net;
using Mediateca.Api.Items;
using Mediateca.Api.Libraries;
using Mediateca.Api.Metadata;
using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Users;
using Mediateca.Infrastructure.Metadata;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Metadatos (fase 4) con AniList, OMDb y las imágenes falsos: la cola, la elección del resultado, Identificar,
/// Actualizar, las imágenes y los permisos. La cola se corre a mano con <see cref="MetadataQueueRunner"/>.
/// </summary>
public sealed class MetadataTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    private const string RegularUser = "invitado";

    private FakeAniListHandler AniList => factory.Metadata.AniList;

    private FakeOmdbHandler Omdb => factory.Metadata.Omdb;

    private FakeImagesHandler Images => factory.Metadata.Images;

    public async Task InitializeAsync()
    {
        await TestUsers.EnsureAdminAsync(factory);
        await TestUsers.EnsureUserAsync(factory, RegularUser);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------- anime

    [Fact]
    public async Task La_cola_busca_el_anime_por_el_nombre_de_la_carpeta_y_aplica_todo()
    {
        AniList.Searches["sousou no frieren"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(154587, "Sousou no Frieren", "Frieren: Beyond Journey's End", 2023),
            MetadataSamples.AniListMedia(
                182255, "Sousou no Frieren 2nd Season", "Frieren: Beyond Journey's End Season 2", 2026,
                status: "RELEASING", nextEpisode: 3, episodeTitles: ["El viaje continúa", "La magia de la calma"]));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, "Sousou no Frieren season 2/01.mp4", "Sousou no Frieren season 2/02.mp4");
        var detailsBefore = AniList.DetailCount;

        await RunQueueAsync();

        var series = await SingleItemAsync(admin, library.Id);
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!;
        Assert.Equal("Matched", detail.MetadataStatus);
        Assert.Equal("Frieren: Beyond Journey's End Season 2", detail.Title);
        Assert.Equal("Sousou no Frieren 2nd Season", detail.OriginalTitle);
        Assert.Equal(2026, detail.Year);
        Assert.Null(detail.EndYear);
        Assert.Equal("Airing", detail.Status);
        Assert.Equal(8.4, detail.CommunityRating);
        Assert.Equal(24, detail.RuntimeMinutes);
        Assert.Equal("Una historia larga.\n\nCon dos párrafos.", detail.Overview);
        Assert.Contains("Fantasy", detail.Genres);
        Assert.Equal(new[] { "Elf" }, detail.Tags!.ToArray());
        Assert.Equal(new[] { "MADHOUSE" }, detail.Studios!.ToArray());
        Assert.Equal(3, detail.NextAiringEpisode);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000000), detail.NextAiringAt);
        Assert.NotNull(detail.MetadataUpdatedAt);
        Assert.Contains(detail.Links!, link => link.Site == "AniList" && link.Url == "https://anilist.co/anime/182255");
        Assert.Contains(detail.Links!, link => link.Site == "MyAnimeList" && link.Url == "https://myanimelist.net/anime/183255");

        // Una búsqueda y nada más: el resultado ya traía el detalle completo.
        Assert.Equal(1, AniList.SearchTerms.Count(term => term == "sousou no frieren"));
        Assert.Equal(detailsBefore, AniList.DetailCount);

        // Las imágenes se descargaron una vez y se sirven desde la carpeta de datos.
        Assert.True(detail.HasImage);
        Assert.True(detail.HasBackdrop);
        var poster = await admin.GetAsync($"/api/items/{series.Id}/image");
        Assert.Equal(HttpStatusCode.OK, poster.StatusCode);
        Assert.Equal("image/jpeg", poster.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            FakeImagesHandler.JpegFor("https://s4.anilist.co/cover/182255.jpg"),
            await poster.Content.ReadAsByteArrayAsync());
        var backdrop = await admin.GetAsync($"/api/items/{series.Id}/image?type=backdrop");
        Assert.Equal(HttpStatusCode.OK, backdrop.StatusCode);
        Assert.Equal(
            FakeImagesHandler.JpegFor("https://s4.anilist.co/banner/182255.jpg"),
            await backdrop.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/items/{series.Id}/image")).StatusCode);
        Assert.Equal(2, Images.Requests.Count(url => url.Contains("/182255.jpg", StringComparison.Ordinal)));

        // Los episodios muestran su título del proveedor; el que no tiene sigue como estaba.
        var episodes = await EpisodesAsync(admin, series.Id);
        Assert.Equal(2, episodes.Count);
        Assert.Equal(new[] { "El viaje continúa", "La magia de la calma" }, episodes.Select(e => e.Title).ToArray());
    }

    [Fact]
    public async Task Los_titulos_de_episodios_van_a_todas_las_carpetas_de_la_serie_Lat_y_Sub()
    {
        AniList.Searches["fire force"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(38671, "Enen no Shouboutai", "Fire Force", 2019),
            MetadataSamples.AniListMedia(
                124194, "Enen no Shouboutai: San no Shou", "Fire Force Season 3", 2025,
                episodeTitles: ["Primero", "Segundo"]));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(
            admin,
            LibraryType.Anime,
            "Fire Force Seasson 3/Lat/01.mp4",
            "Fire Force Seasson 3/Lat/02.mp4",
            "Fire Force Seasson 3/Sub/01.mp4",
            "Fire Force Seasson 3/Sub/02.mp4");

        await RunQueueAsync();

        var series = await SingleItemAsync(admin, library.Id);
        Assert.Equal("Fire Force Season 3", (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!.Title);
        var episodes = await EpisodesAsync(admin, series.Id);
        Assert.Equal(4, episodes.Count);
        Assert.Equal(new[] { "Primero", "Primero", "Segundo", "Segundo" }, episodes.Select(e => e.Title).Order().ToArray());
    }

    [Fact]
    public async Task Una_carpeta_con_Lat_mp4_y_Sub_mp4_prefiere_la_pelicula()
    {
        // Sin año ni la palabra "movie" en el nombre, la serie y la película empatan; Lat.mp4 y Sub.mp4 deciden.
        AniList.Searches["boku no kokoro no yabai yatsu"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(
                162789, "Boku no Kokoro no Yabai Yatsu", "The Dangers in My Heart", 2023),
            MetadataSamples.AniListMedia(
                190000, "Boku no Kokoro no Yabai Yatsu Movie", "The Dangers in My Heart: The Movie", 2026, format: "MOVIE"));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, "Boku no Kokoro no Yabai Yatsu/Lat.mp4", "Boku no Kokoro no Yabai Yatsu/Sub.mp4");

        await RunQueueAsync();

        var series = await SingleItemAsync(admin, library.Id);
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!;
        Assert.Equal("The Dangers in My Heart: The Movie", detail.Title);
    }

    [Fact]
    public async Task Sin_coincidencia_queda_NotFound_con_un_mensaje_y_la_biblioteca_lo_cuenta()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, "Zzzz Qqqq Wwww/01.mp4");

        await RunQueueAsync();

        var series = await SingleItemAsync(admin, library.Id);
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!;
        Assert.Equal("NotFound", detail.MetadataStatus);
        Assert.Contains("Zzzz Qqqq Wwww", detail.MetadataMessage);
        Assert.Equal("Zzzz Qqqq Wwww", detail.Title);

        var summary = (await AdminLibraryAsync(admin, library.Id)).Metadata!;
        Assert.Equal("anilist", summary.Provider);
        Assert.Equal(1, summary.NotFound);
        Assert.Equal(0, summary.Pending);
        Assert.Equal(0, summary.Matched);
        Assert.True(summary.Configured);
    }

    [Fact]
    public async Task Las_imagenes_locales_mandan_sobre_las_del_proveedor()
    {
        AniList.Searches["zeta local"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(777001, "Zeta Local", "Zeta Local", 2026));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Anime", "Zeta Local/01.mp4");
        LibraryTestSupport.WriteFile(folder, "Zeta Local/folder.jpg", [1, 2, 3, 4]);
        LibraryTestSupport.WriteFile(folder, "Zeta Local/backdrop.jpg", [5, 6, 7, 8]);
        var library = await LibraryTestSupport.CreateLibraryAsync(
            admin, LibraryTestSupport.UniqueName("Anime"), LibraryType.Anime, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);

        var series = await SingleItemAsync(admin, library.Id);
        Assert.True(series.HasImage);
        Assert.True((await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!.HasBackdrop);

        await RunQueueAsync();

        Assert.DoesNotContain(Images.Requests, url => url.Contains("/777001.jpg", StringComparison.Ordinal));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await admin.GetByteArrayAsync($"/api/items/{series.Id}/image"));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, await admin.GetByteArrayAsync($"/api/items/{series.Id}/image?type=backdrop"));
        Assert.Equal("Matched", (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!.MetadataStatus);
    }

    [Fact]
    public async Task Solo_se_descargan_imagenes_de_servidores_conocidos()
    {
        AniList.Searches["yyyy servidor raro"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(
                777002, "Yyyy Servidor Raro", "Yyyy Servidor Raro", 2026,
                cover: "https://evil.test/cover.jpg", banner: "http://s4.anilist.co/banner.jpg"));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, "Yyyy Servidor Raro/01.mp4");

        await RunQueueAsync();

        var series = await SingleItemAsync(admin, library.Id);
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!;
        Assert.Equal("Matched", detail.MetadataStatus);
        Assert.False(detail.HasImage);
        Assert.False(detail.HasBackdrop);
        Assert.DoesNotContain(Images.Requests, url => url.Contains("evil.test", StringComparison.Ordinal));
        Assert.DoesNotContain(Images.Requests, url => url.StartsWith("http://", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{series.Id}/image")).StatusCode);
    }

    [Fact]
    public async Task Volver_a_escanear_no_borra_los_metadatos_ni_duplica_la_serie()
    {
        AniList.Searches["wwww rescan"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(777003, "Wwww Rescan", "Wwww Rescan English", 2026, episodeTitles: ["Uno", "Dos", "Tres"]));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Anime", "Wwww Rescan/01.mp4", "Wwww Rescan/02.mp4");
        var library = await LibraryTestSupport.CreateLibraryAsync(
            admin, LibraryTestSupport.UniqueName("Anime"), LibraryType.Anime, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        await RunQueueAsync();
        var series = await SingleItemAsync(admin, library.Id);

        LibraryTestSupport.WriteFile(folder, "Wwww Rescan/03.mp4", []);
        var rescan = await LibraryTestSupport.ScanAsync(admin, library.Id);

        Assert.Equal(1, rescan.Added);
        var after = await SingleItemAsync(admin, library.Id);
        Assert.Equal(series.Id, after.Id);
        Assert.Equal("Wwww Rescan English", after.Title);
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!;
        Assert.Equal("Matched", detail.MetadataStatus);
        Assert.Equal("Wwww Rescan", detail.OriginalTitle);
        Assert.True(detail.HasImage);

        // Los episodios que ya tenían título lo conservan; el nuevo lo recibe en la siguiente actualización.
        var episodes = await EpisodesAsync(admin, series.Id);
        Assert.Equal(3, episodes.Count);
        Assert.Contains("Uno", episodes.Select(e => e.Title));
        Assert.Contains("Dos", episodes.Select(e => e.Title));
        Assert.DoesNotContain("Tres", episodes.Select(e => e.Title));
    }

    [Fact]
    public async Task Una_segunda_busqueda_del_mismo_nombre_sale_de_la_cache()
    {
        AniList.Searches["vvvv cache"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(777004, "Vvvv Cache", "Vvvv Cache", 2026));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, "Vvvv Cache/01.mp4");
        await RunQueueAsync();
        var series = await SingleItemAsync(admin, library.Id);
        var requests = AniList.RequestCount;

        await SetStatusAsync(series.Id, MetadataStatus.Pending, clearIds: true);
        await RunQueueAsync();

        Assert.Equal(requests, AniList.RequestCount);
        Assert.Equal("Matched", (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!.MetadataStatus);
    }

    // ---------------------------------------------------------------- IMDb (OMDb)

    [Fact]
    public async Task Una_pelicula_se_busca_en_OMDb_con_el_titulo_y_el_ano_y_se_descarga_su_poster()
    {
        Omdb.Searches["big buck bunny"] = MetadataSamples.OmdbSearch(
            ("tt1254207", "Big Buck Bunny", "2008", "movie"),
            ("tt0000001", "Big Buck Bunny: Otra", "1999", "movie"));
        Omdb.Titles["tt1254207"] = MetadataSamples.OmdbTitle("tt1254207", "Big Buck Bunny", "2008", rated: "Not Rated");
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Movies, "Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4");

        await RunQueueAsync();

        var movie = await SingleItemAsync(admin, library.Id);
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{movie.Id}"))!;
        Assert.Equal("Matched", detail.MetadataStatus);
        Assert.Equal("Big Buck Bunny", detail.Title);
        Assert.Equal(2008, detail.Year);
        Assert.Equal("Un conejo enorme se defiende de tres roedores.", detail.Overview);
        Assert.Null(detail.ContentRating);
        Assert.Equal(7.2, detail.CommunityRating);
        Assert.Equal(10, detail.RuntimeMinutes);
        Assert.Equal(new[] { "Animation", "Comedy" }, detail.Genres.ToArray());
        Assert.Equal(new[] { "Sacha Goedegebure" }, detail.Directors!.ToArray());
        Assert.Contains(detail.Links!, link => link.Site == "IMDb" && link.Url == "https://www.imdb.com/title/tt1254207/");
        Assert.True(detail.HasImage);
        Assert.False(detail.HasBackdrop);
        Assert.Equal("clave-de-prueba", Omdb.LastApiKey);

        var search = Omdb.Requests.First(request => request.GetValueOrDefault("s") == "Big Buck Bunny");
        Assert.Equal("Big Buck Bunny", search["s"]);
        Assert.Equal("2008", search["y"]);
        Assert.Equal("movie", search["type"]);
        Assert.Contains(Omdb.Requests, request => request.GetValueOrDefault("i") == "tt1254207" && request.GetValueOrDefault("plot") == "full");
    }

    [Fact]
    public async Task Una_serie_toma_los_titulos_de_los_episodios_de_cada_temporada_de_OMDb()
    {
        Omdb.Searches["severance"] = MetadataSamples.OmdbSearch(("tt11280740", "Severance", "2022–", "series"));
        Omdb.Titles["tt11280740"] = MetadataSamples.OmdbTitle(
            "tt11280740", "Severance", "2022–", type: "series", rated: "TV-MA", runtime: "55 min", totalSeasons: "2");
        Omdb.Seasons["tt11280740:1"] = MetadataSamples.OmdbSeason("tt11280740", 1, "Good News About Hell", "Half Loop");
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(
            admin,
            LibraryType.Series,
            "Severance (2022)/Temporada 01/Severance S01E01.mkv",
            "Severance (2022)/Temporada 01/Severance S01E02.mkv");

        await RunQueueAsync();

        var series = await SingleItemAsync(admin, library.Id);
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!;
        Assert.Equal("Matched", detail.MetadataStatus);
        Assert.Equal("Airing", detail.Status);
        Assert.Equal("TV-MA", detail.ContentRating);
        Assert.Contains(Omdb.Requests, request => request.GetValueOrDefault("s") == "Severance" && request.GetValueOrDefault("type") == "series");
        Assert.Contains(Omdb.Requests, request => request.GetValueOrDefault("Season") == "1");
        var episodes = await EpisodesAsync(admin, series.Id);
        Assert.Equal(new[] { "Good News About Hell", "Half Loop" }, episodes.Select(e => e.Title).Order().ToArray());
    }

    // ---------------------------------------------------------------- Identificar y Actualizar

    [Fact]
    public async Task Identificar_con_una_direccion_de_AniList_aplica_ese_resultado_al_momento()
    {
        AniList.Media[154587] = MetadataSamples.AniListMedia(154587, "Sousou no Frieren", "Frieren: Beyond Journey's End", 2023);
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, "Qqqq Desconocido Total/01.mp4");
        await RunQueueAsync();
        var series = await SingleItemAsync(admin, library.Id);
        Assert.Equal("NotFound", (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!.MetadataStatus);

        var response = await admin.PostJsonAsync(
            $"/api/admin/items/{series.Id}/metadata/identify",
            new IdentifyRequest("https://anilist.co/anime/154587/Sousou-no-Frieren"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.ReadJsonAsync<MetadataResultResponse>())!;
        Assert.Equal("Matched", result.Status);
        var detail = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!;
        Assert.Equal("Frieren: Beyond Journey's End", detail.Title);
        Assert.Equal("Matched", detail.MetadataStatus);
        Assert.Null(detail.MetadataMessage);
        Assert.True(detail.HasImage);

        // Con el id guardado, "Actualizar" ya no busca: pide el detalle sin usar la cache.
        var detailsBefore = AniList.DetailCount;
        AniList.Media[154587] = MetadataSamples.AniListMedia(
            154587, "Sousou no Frieren", "Frieren: Beyond Journey's End", 2023, status: "RELEASING", nextEpisode: 9);
        var refreshed = await admin.PostAsync($"/api/admin/items/{series.Id}/metadata/refresh", null);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(detailsBefore + 1, AniList.DetailCount);
        var after = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!;
        Assert.Equal("Airing", after.Status);
        Assert.Equal(9, after.NextAiringEpisode);
    }

    [Theory]
    [InlineData("tt0133093", "AniList")]
    [InlineData("hola, esto no es un id", "No reconozco")]
    [InlineData("https://example.com/anime/21", "No reconozco")]
    [InlineData("999999999", "no tiene el identificador")]
    public async Task Identificar_con_algo_que_no_sirve_responde_400_con_el_motivo(string texto, string motivo)
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, $"Rrrr Invalido {Guid.NewGuid():N}/01.mp4");
        var series = await SingleItemAsync(admin, library.Id);

        var response = await admin.PostJsonAsync($"/api/admin/items/{series.Id}/metadata/identify", new IdentifyRequest(texto));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(motivo, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task La_busqueda_de_Identificar_devuelve_los_resultados_del_proveedor_de_la_biblioteca()
    {
        AniList.Searches["frieren"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(5, "Otro Anime", "Otro Anime", 2001),
            MetadataSamples.AniListMedia(154587, "Sousou no Frieren", "Frieren", 2023));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, $"Tttt Busqueda {Guid.NewGuid():N}/01.mp4");
        var series = await SingleItemAsync(admin, library.Id);

        var search = (await admin.GetJsonAsync<MetadataSearchResponse>(
            $"/api/admin/items/{series.Id}/metadata/search?query=frieren"))!;

        Assert.Equal("anilist", search.Provider);
        Assert.Equal("AniList", search.ProviderName);
        Assert.Equal(2, search.Results.Count);
        Assert.Equal("154587", search.Results[0].ExternalId);
        Assert.Equal("Frieren", search.Results[0].Title);
        Assert.Equal(2023, search.Results[0].Year);
        Assert.Equal("TV", search.Results[0].Format);
        Assert.Equal("https://anilist.co/anime/154587", search.Results[0].Url);
        Assert.StartsWith("https://s4.anilist.co/", search.Results[0].PosterUrl);
    }

    [Fact]
    public async Task Identificar_y_Actualizar_son_solo_para_el_administrador_y_no_existen_para_lo_que_no_es_una_serie_o_pelicula()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, $"Uuuu Permisos {Guid.NewGuid():N}/Lat/01.mp4");
        var series = await SingleItemAsync(admin, library.Id);
        var season = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{series.Id}/children"))!.First();
        using var user = await TestUsers.LoginAsync(factory, RegularUser);
        await GrantAccessAsync(RegularUser, library.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"/api/admin/items/{series.Id}/metadata/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.PostJsonAsync($"/api/admin/items/{series.Id}/metadata/identify", new IdentifyRequest("21"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"/api/admin/items/{series.Id}/metadata/search")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"/api/admin/libraries/{library.Id}/metadata/fetch", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/items/{Guid.NewGuid()}/metadata/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/items/{season.Id}/metadata/refresh", null)).StatusCode);

        // El mensaje del proveedor solo lo ve el administrador.
        await SetStatusAsync(series.Id, MetadataStatus.Error, message: "AniList respondió con un error (500).");
        Assert.Equal("AniList respondió con un error (500).", (await admin.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!.MetadataMessage);
        Assert.Null((await user.GetJsonAsync<ItemDetail>($"/api/items/{series.Id}"))!.MetadataMessage);
    }

    // ---------------------------------------------------------------- cola e imagen: permisos

    [Fact]
    public async Task Buscar_metadatos_pone_en_la_cola_lo_pendiente_y_los_errores_pero_no_lo_que_no_se_encontro()
    {
        AniList.Searches["ssss cola"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(777005, "Ssss Cola", "Ssss Cola", 2026));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(
            factory, "Anime", "Ssss Cola/01.mp4", "Ssss Sin Resultado/01.mp4", "Ssss Con Error/01.mp4");
        var library = await LibraryTestSupport.CreateLibraryAsync(
            admin, LibraryTestSupport.UniqueName("Anime"), LibraryType.Anime, folder);
        await LibraryTestSupport.ScanAsync(admin, library.Id);
        var items = (await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items;
        var queued = items.Single(i => i.Title == "Ssss Cola");
        var notFound = items.Single(i => i.Title == "Ssss Sin Resultado");
        var failed = items.Single(i => i.Title == "Ssss Con Error");
        await SetStatusAsync(notFound.Id, MetadataStatus.NotFound, message: "No se encontró.");
        await SetStatusAsync(failed.Id, MetadataStatus.Error, message: "AniList respondió con un error (500).");
        var before = (await AdminLibraryAsync(admin, library.Id)).Metadata!;
        Assert.Equal((1, 0, 1, 1), (before.Pending, before.Matched, before.NotFound, before.Error));

        var response = await admin.PostAsync($"/api/admin/libraries/{library.Id}/metadata/fetch", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var queue = (await response.ReadJsonAsync<MetadataQueueResponse>())!;
        Assert.Equal(2, queue.Queued);
        Assert.True(queue.Configured);
        var after = (await AdminLibraryAsync(admin, library.Id)).Metadata!;
        Assert.Equal((2, 0, 1, 0), (after.Pending, after.Matched, after.NotFound, after.Error));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/libraries/{Guid.NewGuid()}/metadata/fetch", null)).StatusCode);

        await RunQueueAsync();

        // La que estaba pendiente se encontró; la que dio error se volvió a buscar y esta vez no hubo resultados.
        var done = (await AdminLibraryAsync(admin, library.Id)).Metadata!;
        Assert.Equal((0, 1, 2, 0), (done.Pending, done.Matched, done.NotFound, done.Error));
        Assert.Equal("Matched", (await admin.GetJsonAsync<ItemDetail>($"/api/items/{queued.Id}"))!.MetadataStatus);
    }

    [Fact]
    public async Task La_imagen_de_fondo_tiene_las_mismas_reglas_de_acceso_que_el_poster()
    {
        AniList.Searches["xxxx acceso"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(777006, "Xxxx Acceso", "Xxxx Acceso", 2026));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var library = await ScanAsync(admin, LibraryType.Anime, "Xxxx Acceso/Lat/01.mp4");
        await RunQueueAsync();
        var series = await SingleItemAsync(admin, library.Id);
        var season = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{series.Id}/children"))!
            .First(child => child.Kind == "Season");
        using var user = await TestUsers.LoginAsync(factory, RegularUser);
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/items/{series.Id}/image?type=backdrop")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/items/{series.Id}/image?type=backdrop")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/items/{series.Id}/image")).StatusCode);

        await GrantAccessAsync(RegularUser, library.Id);

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/items/{series.Id}/image?type=backdrop")).StatusCode);
        // La temporada no tiene fondo propio: muestra el de su serie.
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/items/{season.Id}/image?type=backdrop")).StatusCode);
        Assert.True((await user.GetJsonAsync<ItemDetail>($"/api/items/{season.Id}"))!.HasBackdrop);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/items/{series.Id}/image?type=otra")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/items/{Guid.NewGuid()}/image?type=backdrop")).StatusCode);
    }

    // ---------------------------------------------------------------- refresco de las series en emisión

    [Fact]
    public async Task El_refresco_actualiza_solo_las_series_en_emision_con_mas_de_24_horas()
    {
        AniList.Searches["pppp emision"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(777007, "Pppp Emision", "Pppp Emision", 2026, status: "RELEASING", nextEpisode: 4));
        AniList.Searches["pppp terminada"] = MetadataSamples.AniListArray(
            MetadataSamples.AniListMedia(777008, "Pppp Terminada", "Pppp Terminada", 2020));
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var airing = await ScanAsync(admin, LibraryType.Anime, "Pppp Emision/01.mp4");
        var finished = await ScanAsync(admin, LibraryType.Anime, "Pppp Terminada/01.mp4");
        await RunQueueAsync();
        var airingId = (await SingleItemAsync(admin, airing.Id)).Id;
        var finishedId = (await SingleItemAsync(admin, finished.Id)).Id;
        var refresh = factory.Services.GetRequiredService<MetadataRefreshService>();

        // Recién encontradas, no toca refrescar ninguna.
        var detailsBefore = AniList.DetailCount;
        Assert.Equal(0, await refresh.RefreshDueAsync(CancellationToken.None));
        Assert.Equal(detailsBefore, AniList.DetailCount);

        // Pasaron dos días y AniList ya anuncia el episodio 5.
        await AgeMetadataAsync(airingId);
        await AgeMetadataAsync(finishedId);
        AniList.Media[777007] = MetadataSamples.AniListMedia(
            777007, "Pppp Emision", "Pppp Emision", 2026, status: "RELEASING", nextEpisode: 5);
        AniList.Media[777008] = MetadataSamples.AniListMedia(777008, "Pppp Terminada", "Pppp Terminada", 2020);

        Assert.Equal(1, await refresh.RefreshDueAsync(CancellationToken.None));

        Assert.Equal(detailsBefore + 1, AniList.DetailCount);
        Assert.Equal(5, (await admin.GetJsonAsync<ItemDetail>($"/api/items/{airingId}"))!.NextAiringEpisode);
        Assert.True((await admin.GetJsonAsync<ItemDetail>($"/api/items/{airingId}"))!.MetadataUpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.True((await admin.GetJsonAsync<ItemDetail>($"/api/items/{finishedId}"))!.MetadataUpdatedAt < DateTimeOffset.UtcNow.AddDays(-1));
    }

    // ---------------------------------------------------------------- utilidades

    private Task<int> RunQueueAsync() =>
        factory.Services.GetRequiredService<MetadataQueueRunner>().RunUntilIdleAsync(CancellationToken.None);

    private async Task<AdminLibraryResponse> ScanAsync(HttpClient admin, LibraryType type, params string[] files)
    {
        var folder = LibraryTestSupport.CreateFolder(factory, type.ToString(), files);
        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName(type.ToString()), type, folder);
        var scan = await LibraryTestSupport.ScanAsync(admin, library.Id);
        Assert.Equal(0, scan.Errors);
        return library;
    }

    private static async Task<ItemSummary> SingleItemAsync(HttpClient admin, Guid libraryId) =>
        Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{libraryId}/items"))!.Items);

    private static async Task<AdminLibraryResponse> AdminLibraryAsync(HttpClient admin, Guid libraryId) =>
        (await admin.GetJsonAsync<List<AdminLibraryResponse>>("/api/admin/libraries"))!.Single(l => l.Id == libraryId);

    // Los episodios de una serie, estén directo en ella o dentro de sus carpetas (Lat, Sub, temporadas).
    private static async Task<List<ChildItem>> EpisodesAsync(HttpClient admin, Guid seriesId)
    {
        var episodes = new List<ChildItem>();
        foreach (var child in (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{seriesId}/children"))!)
        {
            if (child.Kind == "Episode")
            {
                episodes.Add(child);
            }
            else if (child.Kind == "Season")
            {
                episodes.AddRange(await EpisodesAsync(admin, child.Id));
            }
        }

        return episodes;
    }

    private async Task SetStatusAsync(Guid id, MetadataStatus status, string? message = null, bool clearIds = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        await db.MediaItems.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.MetadataStatus, status)
            .SetProperty(item => item.MetadataMessage, message));
        if (clearIds)
        {
            // Como una serie que nunca se identificó: la cola vuelve a buscarla por su nombre.
            await db.MediaItems.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.AniListId, (int?)null));
        }
    }

    private async Task AgeMetadataAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        var old = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-2);
        await db.MediaItems.Where(item => item.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.MetadataUpdatedAt, old));
    }

    private async Task GrantAccessAsync(string userName, Guid libraryId)
    {
        var userId = await TestUsers.GetUserIdAsync(factory, userName);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        if (!await db.LibraryAccess.AnyAsync(a => a.UserId == userId && a.LibraryId == libraryId))
        {
            db.LibraryAccess.Add(new UserLibraryAccess { UserId = userId, LibraryId = libraryId });
            await db.SaveChangesAsync();
        }
    }
}
