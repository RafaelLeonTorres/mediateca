using System.Net;
using System.Text;
using Mediateca.Api.Items;
using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Subtitles;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Subtítulos (PLAN.md, secciones 9 y 10): la lista de pistas y su descarga en WebVTT, con archivos sueltos de
/// verdad y con ffprobe y ffmpeg falsos (<see cref="FakeProcessRunner"/>), así que no necesitan FFmpeg instalado.
/// </summary>
public sealed class SubtitleTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    private const string Srt = "1\r\n00:00:01,500 --> 00:00:03,000\r\nHola, mundo\r\n\r\n2\r\n00:00:04,000 --> 00:00:05,250\r\n<i>Adiós</i>\r\n";

    private const string ProbeJson = """
        { "streams": [
          { "index": 2, "codec_name": "mov_text", "codec_type": "subtitle", "disposition": { "default": 1, "forced": 0 }, "tags": { "language": "eng" } },
          { "index": 3, "codec_name": "mov_text", "codec_type": "subtitle", "disposition": { "default": 0, "forced": 0 }, "tags": { "language": "spa" } },
          { "index": 4, "codec_name": "hdmv_pgs_subtitle", "codec_type": "subtitle", "tags": { "language": "jpn" } }
        ] }
        """;

    private const string ExtractedVtt = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHola desde ffmpeg\n\n";

    public async Task InitializeAsync()
    {
        await TestUsers.EnsureAdminAsync(factory);
        factory.Processes.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Una serie "Mi Anime" con dos episodios en la carpeta Lat y los archivos extra que pida la prueba.</summary>
    private async Task<(HttpClient Admin, string Folder, ChildItem First, ChildItem Second)> CreateAnimeAsync(
        params (string Path, string Content)[] extraFiles)
    {
        var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var folder = LibraryTestSupport.CreateFolder(factory, "Anime", "Mi Anime/Lat/01.mp4", "Mi Anime/Lat/02.mp4");
        foreach (var (path, content) in extraFiles)
        {
            LibraryTestSupport.WriteFile(folder, path, Encoding.UTF8.GetBytes(content));
        }

        var library = await LibraryTestSupport.CreateLibraryAsync(admin, LibraryTestSupport.UniqueName("Anime"), LibraryType.Anime, folder);
        var scan = await LibraryTestSupport.ScanAsync(admin, library.Id);
        Assert.Equal(2, scan.Added);

        var series = Assert.Single((await admin.GetJsonAsync<ItemPage>($"/api/libraries/{library.Id}/items"))!.Items);
        var groups = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{series.Id}/children"))!;
        var episodes = (await admin.GetJsonAsync<List<ChildItem>>($"/api/items/{Assert.Single(groups).Id}/children"))!;
        return (admin, folder, episodes[0], episodes[1]);
    }

    private static Task<List<SubtitleTrackResponse>?> TracksAsync(HttpClient client, Guid id) =>
        client.GetJsonAsync<List<SubtitleTrackResponse>>($"/api/items/{id}/subtitles");

    private static ProcessResult Ok(string output) => new(ProcessOutcome.Completed, 0, output, string.Empty);

    [Fact]
    public async Task Lista_y_descarga_un_srt_suelto_junto_al_video()
    {
        var (admin, _, first, second) = await CreateAnimeAsync(
            ("Mi Anime/Lat/01.es.srt", Srt),
            ("Mi Anime/Lat/01.en.srt", Srt),
            ("Mi Anime/Lat/02.srt", Srt));
        using var adminScope = admin;

        var tracks = (await TracksAsync(admin, first.Id))!;

        // En orden de nombre de archivo: 01.en.srt y 01.es.srt. El de 02 no es de este episodio.
        Assert.Equal(new[] { "x0", "x1" }, tracks.Select(t => t.Id).ToArray());
        Assert.Equal(new[] { "Inglés", "Español" }, tracks.Select(t => t.Label).ToArray());
        var spanish = tracks[1];
        Assert.Equal("es", spanish.Language);
        Assert.Equal("subrip", spanish.Codec);
        Assert.Equal("external", spanish.Source);
        Assert.False(spanish.IsDefault);
        Assert.False(spanish.IsForced);
        Assert.Equal($"/api/items/{first.Id}/subtitles/x1.vtt", spanish.Url);

        using var response = await admin.GetAsync(spanish.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/vtt", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.Equal(
            "WEBVTT\n\n00:00:01.500 --> 00:00:03.000\nHola, mundo\n\n00:00:04.000 --> 00:00:05.250\n<i>Adiós</i>\n\n",
            await response.Content.ReadAsStringAsync());

        // El archivo de 02 no tiene idioma: queda como "Pista 1".
        var other = Assert.Single((await TracksAsync(admin, second.Id))!);
        Assert.Equal("Pista 1", other.Label);
        Assert.Null(other.Language);

        // La respuesta nunca incluye rutas del servidor.
        var json = await admin.GetStringAsync($"/api/items/{first.Id}/subtitles");
        Assert.DoesNotContain(factory.MediaPath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("filePath", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Los_forzados_el_vtt_y_el_ass_sueltos_se_sirven_como_WebVTT()
    {
        var (admin, _, first, _) = await CreateAnimeAsync(
            ("Mi Anime/Lat/01.spa.forced.srt", Srt),
            ("Mi Anime/Lat/01.ja.vtt", "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nこんにちは\n"),
            ("Mi Anime/Lat/01.Spanish.ass", "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\an8}Línea del ASS\\NSegunda\n"));
        using var adminScope = admin;

        var tracks = (await TracksAsync(admin, first.Id))!;

        // En orden de nombre de archivo: 01.ja.vtt, 01.spa.forced.srt y 01.Spanish.ass.
        Assert.Equal(new[] { "x0", "x1", "x2" }, tracks.Select(t => t.Id).ToArray());
        Assert.Equal(new[] { "Japonés", "Español (forzados)", "Español" }, tracks.Select(t => t.Label).ToArray());
        Assert.Equal(new[] { "webvtt", "subrip", "ass" }, tracks.Select(t => t.Codec).ToArray());
        Assert.True(tracks[1].IsForced);
        Assert.False(tracks[2].IsForced);

        // Los archivos .vtt salen tal cual.
        Assert.Equal(
            "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nこんにちは\n",
            await admin.GetStringAsync(tracks[0].Url));

        // Sin ffmpeg (el doble responde "no instalado"), el .ass pasa por el convertidor básico de C#.
        Assert.Equal(
            "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nLínea del ASS\nSegunda\n\n",
            await admin.GetStringAsync(tracks[2].Url));
    }

    [Fact]
    public async Task Una_pista_incrustada_se_extrae_con_el_extractor_falso_y_la_segunda_vez_sale_del_disco()
    {
        factory.Processes.Handler = request => request.FileName switch
        {
            "ffprobe" => Ok(ProbeJson),
            "ffmpeg" => Ok(ExtractedVtt),
            _ => ProcessResult.NotInstalled,
        };
        var (admin, folder, first, _) = await CreateAnimeAsync(("Mi Anime/Lat/01.es.srt", Srt));
        using var adminScope = admin;

        var tracks = (await TracksAsync(admin, first.Id))!;

        // Primero las incrustadas (la de imagen no se lista) y después los archivos sueltos.
        Assert.Equal(new[] { "e2", "e3", "x0" }, tracks.Select(t => t.Id).ToArray());
        Assert.Equal(new[] { "Inglés", "Español (incrustados)", "Español (archivo)" }, tracks.Select(t => t.Label).ToArray());
        Assert.Equal(new[] { "embedded", "embedded", "external" }, tracks.Select(t => t.Source).ToArray());
        Assert.True(tracks[0].IsDefault);
        Assert.Equal("mov_text", tracks[1].Codec);
        var probe = Assert.Single(factory.Processes.RequestsFor("ffprobe"));
        Assert.Equal(
            new[] { "-v", "error", "-print_format", "json", "-show_streams", "-select_streams", "s" },
            probe.Arguments.Take(7).ToArray());
        Assert.True(MediaPaths.AreSame(Path.Combine(folder, "Mi Anime", "Lat", "01.mp4"), probe.Arguments[^1]));

        using var extracted = await admin.GetAsync(tracks[1].Url);

        Assert.Equal(HttpStatusCode.OK, extracted.StatusCode);
        Assert.Equal("text/vtt", extracted.Content.Headers.ContentType?.MediaType);
        Assert.True(extracted.Headers.CacheControl?.Private);
        Assert.Equal(ExtractedVtt, await extracted.Content.ReadAsStringAsync());
        var ffmpeg = Assert.Single(factory.Processes.RequestsFor("ffmpeg"));
        Assert.Equal("0:3", ffmpeg.Arguments[ffmpeg.Arguments.ToList().IndexOf("-map") + 1]);
        Assert.Equal("pipe:1", ffmpeg.Arguments[^1]);

        // Quedó guardado en {DataPath}/subtitles y la segunda vez no vuelve a correr ffmpeg.
        var dataPath = factory.Services.GetRequiredService<IOptions<StorageOptions>>().Value.DataPath;
        var stored = Assert.Single(Directory.GetFiles(Path.Combine(dataPath, "subtitles"), $"{first.Id}-e3-*.vtt"));
        Assert.Equal(ExtractedVtt, await File.ReadAllTextAsync(stored));
        Assert.Equal(ExtractedVtt, await admin.GetStringAsync(tracks[1].Url));
        Assert.Single(factory.Processes.RequestsFor("ffmpeg"));
        Assert.Single(factory.Processes.RequestsFor("ffprobe"));
    }

    [Fact]
    public async Task Sin_ffprobe_ni_ffmpeg_no_falla_nada_y_la_lista_viene_vacia()
    {
        var (admin, _, first, _) = await CreateAnimeAsync();
        using var adminScope = admin;

        using var response = await admin.GetAsync($"/api/items/{first.Id}/subtitles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.ReadJsonAsync<List<SubtitleTrackResponse>>())!);
    }

    [Fact]
    public async Task Si_ffmpeg_falla_la_descarga_responde_502_y_no_guarda_nada()
    {
        factory.Processes.Handler = request => request.FileName == "ffprobe"
            ? Ok(ProbeJson)
            : new ProcessResult(ProcessOutcome.Completed, 1, string.Empty, "Invalid data found when processing input");
        var (admin, _, first, _) = await CreateAnimeAsync();
        using var adminScope = admin;
        var track = (await TracksAsync(admin, first.Id))!.First(t => t.Id == "e2");

        using var response = await admin.GetAsync(track.Url);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var dataPath = factory.Services.GetRequiredService<IOptions<StorageOptions>>().Value.DataPath;
        var cache = Path.Combine(dataPath, "subtitles");
        Assert.True(!Directory.Exists(cache) || Directory.GetFiles(cache, $"{first.Id}-e2-*").Length == 0);
    }

    [Fact]
    public async Task Lo_ajeno_y_lo_que_no_existe_responde_404_y_sin_sesion_401()
    {
        factory.Processes.Handler = request => request.FileName == "ffprobe" ? Ok(ProbeJson) : Ok(ExtractedVtt);
        var (admin, _, first, _) = await CreateAnimeAsync(("Mi Anime/Lat/01.es.srt", Srt));
        using var adminScope = admin;
        var parentId = (await admin.GetJsonAsync<ItemDetail>($"/api/items/{first.Id}"))!.ParentId;
        const string outsider = "sin-acceso-subtitulos";
        await TestUsers.EnsureUserAsync(factory, outsider);
        using var user = await TestUsers.LoginAsync(factory, outsider);

        // Un usuario sin acceso a la biblioteca: 404 en la lista y en cada pista, existan o no.
        foreach (var url in new[]
        {
            $"/api/items/{first.Id}/subtitles",
            $"/api/items/{first.Id}/subtitles/e2.vtt",
            $"/api/items/{first.Id}/subtitles/x0.vtt",
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync(url)).StatusCode);
        }

        // Algo que no existe, una carpeta (no es reproducible) y pistas que no existen.
        foreach (var url in new[]
        {
            $"/api/items/{Guid.NewGuid()}/subtitles",
            $"/api/items/{Guid.NewGuid()}/subtitles/e2.vtt",
            $"/api/items/{parentId}/subtitles",
            $"/api/items/{parentId}/subtitles/e2.vtt",
            $"/api/items/{first.Id}/subtitles/e9.vtt",
            $"/api/items/{first.Id}/subtitles/x5.vtt",
            $"/api/items/{first.Id}/subtitles/nada.vtt",
            $"/api/items/{first.Id}/subtitles/..%2F..%2Fetc%2Fpasswd.vtt",
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(url)).StatusCode);
        }

        using var anonymous = factory.CreateClient();
        foreach (var url in new[] { $"/api/items/{first.Id}/subtitles", $"/api/items/{first.Id}/subtitles/e2.vtt" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task Un_video_fuera_de_las_carpetas_de_medios_responde_404_aunque_exista()
    {
        factory.Processes.Handler = request => request.FileName == "ffprobe" ? Ok(ProbeJson) : Ok(ExtractedVtt);
        var (admin, _, first, _) = await CreateAnimeAsync();
        using var adminScope = admin;
        var outside = Path.Combine(Path.GetTempPath(), $"fuera-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var video = Path.Combine(outside, "01.mp4");
        await File.WriteAllBytesAsync(video, [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(outside, "01.es.srt"), Srt);
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
                var item = await db.MediaItems.OfType<PlayableItem>().SingleAsync(i => i.Id == first.Id);
                item.FilePath = video;
                await db.SaveChangesAsync();
            }

            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{first.Id}/subtitles")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{first.Id}/subtitles/x0.vtt")).StatusCode);
            Assert.Empty(factory.Processes.Requests);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }
}
