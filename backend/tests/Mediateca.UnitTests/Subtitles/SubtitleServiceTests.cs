using Mediateca.Core.Subtitles;
using Mediateca.Infrastructure.Storage;
using Mediateca.Infrastructure.Subtitles;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Mediateca.UnitTests.Subtitles;

/// <summary>
/// El servicio de subtítulos con un ejecutor de procesos falso: ninguna de estas pruebas necesita que ffmpeg o
/// ffprobe estén instalados.
/// </summary>
public sealed class SubtitleServiceTests : IDisposable
{
    private const string ProbeJson = """
        { "streams": [
          { "index": 2, "codec_name": "mov_text", "codec_type": "subtitle", "disposition": { "default": 0, "forced": 0 }, "tags": { "language": "eng" } },
          { "index": 3, "codec_name": "mov_text", "codec_type": "subtitle", "disposition": { "default": 0, "forced": 0 }, "tags": { "language": "spa" } }
        ] }
        """;

    private const string Vtt = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHola\n\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mediateca-subtitulos-{Guid.NewGuid():N}");
    private readonly string _media;
    private readonly string _data;
    private readonly Mock<IProcessRunner> _processes = new();
    private readonly CapturingLogger _logger = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public SubtitleServiceTests()
    {
        _media = Path.Combine(_root, "media");
        _data = Path.Combine(_root, "data");
        Directory.CreateDirectory(_media);
        Directory.CreateDirectory(_data);
    }

    public void Dispose()
    {
        _cache.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SubtitleService CreateService(MediaToolsOptions? tools = null) =>
        new(
            _processes.Object,
            _cache,
            Options.Create(tools ?? new MediaToolsOptions()),
            Options.Create(new StorageOptions { DataPath = _data, MediaRoots = [_media] }),
            _logger);

    private string CreateVideo(string name = "01.mp4")
    {
        var path = Path.Combine(_media, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_media, name), content);

    private void Probe(ProcessResult result) =>
        _processes.Setup(p => p.RunAsync(It.Is<ProcessRequest>(r => r.FileName == "ffprobe"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void Ffmpeg(ProcessResult result) =>
        _processes.Setup(p => p.RunAsync(It.Is<ProcessRequest>(r => r.FileName == "ffmpeg"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private static ProcessResult Ok(string output) => new(ProcessOutcome.Completed, 0, output, string.Empty);

    private static ProcessResult Failed() => new(ProcessOutcome.Completed, 1, string.Empty, "Invalid data");

    private int Calls(string program) =>
        _processes.Invocations.Count(i => i.Arguments[0] is ProcessRequest request && request.FileName == program);

    [Fact]
    public async Task Sondea_con_ffprobe_y_lista_las_pistas_incrustadas_con_su_etiqueta()
    {
        var video = CreateVideo();
        ProcessRequest? request = null;
        _processes.Setup(p => p.RunAsync(It.IsAny<ProcessRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessRequest, CancellationToken>((r, _) => request = r)
            .ReturnsAsync(Ok(ProbeJson));

        var tracks = await CreateService().ListAsync(Guid.NewGuid(), video, CancellationToken.None);

        Assert.Equal(new[] { "e2", "e3" }, tracks.Select(t => t.Id).ToArray());
        Assert.Equal(new[] { "Inglés", "Español" }, tracks.Select(t => t.Label).ToArray());
        Assert.NotNull(request);
        Assert.Equal("ffprobe", request.FileName);
        Assert.Equal(
            new[] { "-v", "error", "-print_format", "json", "-show_streams", "-select_streams", "s", video },
            request.Arguments.ToArray());
    }

    [Fact]
    public async Task Usa_las_rutas_configuradas_de_ffprobe_y_ffmpeg()
    {
        var video = CreateVideo();
        _processes.Setup(p => p.RunAsync(It.Is<ProcessRequest>(r => r.FileName == "/opt/ff/ffprobe"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(ProbeJson));
        _processes.Setup(p => p.RunAsync(It.Is<ProcessRequest>(r => r.FileName == "/opt/ff/ffmpeg"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Vtt));
        var service = CreateService(new MediaToolsOptions { Ffprobe = "/opt/ff/ffprobe", Ffmpeg = "/opt/ff/ffmpeg" });

        Assert.Equal(2, (await service.ListAsync(Guid.NewGuid(), video, CancellationToken.None)).Count);
        Assert.Equal(SubtitleStatus.Ok, (await service.GetVttAsync(Guid.NewGuid(), video, "e2", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Sin_ffprobe_instalado_no_falla_solo_faltan_las_incrustadas_y_avisa_una_sola_vez()
    {
        var first = CreateVideo("01.mp4");
        var second = CreateVideo("02.mp4");
        WriteFile("01.es.srt", "1\n00:00:01,000 --> 00:00:02,000\nHola\n");
        Probe(ProcessResult.NotInstalled);
        var service = CreateService();

        var withExternal = await service.ListAsync(Guid.NewGuid(), first, CancellationToken.None);
        var withoutAnything = await service.ListAsync(Guid.NewGuid(), second, CancellationToken.None);

        Assert.Equal("Español", Assert.Single(withExternal).Label);
        Assert.Empty(withoutAnything);
        var warnings = _logger.Messages.Where(m => m.Level == LogLevel.Warning && m.Text.Contains("ffprobe")).ToList();
        Assert.Single(warnings);
    }

    [Fact]
    public async Task La_lista_se_guarda_en_memoria_por_ruta_y_fecha()
    {
        var video = CreateVideo();
        Probe(Ok(ProbeJson));
        var service = CreateService();

        await service.ListAsync(Guid.NewGuid(), video, CancellationToken.None);
        await service.ListAsync(Guid.NewGuid(), video, CancellationToken.None);
        Assert.Equal(1, Calls("ffprobe"));

        // Si el archivo cambia (otra fecha de modificación), se vuelve a sondear.
        File.SetLastWriteTimeUtc(video, DateTime.UtcNow.AddMinutes(-5));
        await service.ListAsync(Guid.NewGuid(), video, CancellationToken.None);
        Assert.Equal(2, Calls("ffprobe"));
    }

    [Fact]
    public async Task Un_subtitulo_suelto_nuevo_en_la_carpeta_se_ve_sin_esperar()
    {
        var video = CreateVideo();
        Probe(Ok("""{ "streams": [] }"""));
        var service = CreateService();
        Assert.Empty(await service.ListAsync(Guid.NewGuid(), video, CancellationToken.None));

        WriteFile("01.es.srt", "1\n00:00:01,000 --> 00:00:02,000\nHola\n");
        // La fecha de la carpeta tiene la resolución del reloj del sistema: se mueve para que sea distinta.
        Directory.SetLastWriteTimeUtc(_media, DateTime.UtcNow.AddMinutes(1));

        Assert.Single(await service.ListAsync(Guid.NewGuid(), video, CancellationToken.None));
    }

    [Fact]
    public async Task Si_ffprobe_falla_no_se_guarda_en_memoria_por_mucho_pero_tampoco_lanza()
    {
        var video = CreateVideo();
        Probe(Failed());

        var tracks = await CreateService().ListAsync(Guid.NewGuid(), video, CancellationToken.None);

        Assert.Empty(tracks);
        Assert.Contains(_logger.Messages, m => m.Level == LogLevel.Warning && m.Text.Contains("ffprobe"));
    }

    [Fact]
    public async Task Lo_que_no_es_un_video_o_no_existe_no_se_sondea()
    {
        var service = CreateService();

        Assert.Empty(await service.ListAsync(Guid.NewGuid(), Path.Combine(_media, "no-existe.mp4"), CancellationToken.None));
        var audio = Path.Combine(_media, "pista.mp3");
        File.WriteAllBytes(audio, []);
        Assert.Empty(await service.ListAsync(Guid.NewGuid(), audio, CancellationToken.None));
        Assert.Equal(0, Calls("ffprobe"));
    }

    [Fact]
    public async Task Extrae_una_pista_incrustada_con_ffmpeg_sin_shell_y_la_guarda_en_disco_para_la_proxima()
    {
        var video = CreateVideo();
        var itemId = Guid.NewGuid();
        Probe(Ok(ProbeJson));
        ProcessRequest? request = null;
        _processes.Setup(p => p.RunAsync(It.Is<ProcessRequest>(r => r.FileName == "ffmpeg"), It.IsAny<CancellationToken>()))
            .Callback<ProcessRequest, CancellationToken>((r, _) => request = r)
            .ReturnsAsync(Ok(Vtt));
        var service = CreateService();

        var first = await service.GetVttAsync(itemId, video, "e3", CancellationToken.None);
        var second = await service.GetVttAsync(itemId, video, "e3", CancellationToken.None);

        Assert.Equal(SubtitleStatus.Ok, first.Status);
        Assert.Equal(Vtt, first.Content);
        Assert.Equal(Vtt, second.Content);
        Assert.Equal(1, Calls("ffmpeg"));
        Assert.NotNull(request);
        Assert.Equal(
            new[] { "-v", "error", "-nostdin", "-i", video, "-map", "0:3", "-f", "webvtt", "pipe:1" },
            request.Arguments.ToArray());
        var stored = Assert.Single(Directory.GetFiles(Path.Combine(_data, "subtitles")));
        Assert.Equal($"{itemId}-e3-{File.GetLastWriteTimeUtc(video).Ticks}.vtt", Path.GetFileName(stored));
    }

    [Fact]
    public async Task Si_el_video_cambia_borra_la_extraccion_vieja()
    {
        var video = CreateVideo();
        var itemId = Guid.NewGuid();
        Probe(Ok(ProbeJson));
        Ffmpeg(Ok(Vtt));
        var service = CreateService();
        await service.GetVttAsync(itemId, video, "e2", CancellationToken.None);

        File.SetLastWriteTimeUtc(video, DateTime.UtcNow.AddMinutes(-10));
        await service.GetVttAsync(itemId, video, "e2", CancellationToken.None);

        Assert.Equal(2, Calls("ffmpeg"));
        Assert.Single(Directory.GetFiles(Path.Combine(_data, "subtitles")));
    }

    [Fact]
    public async Task Si_ffmpeg_falla_o_no_esta_instalado_lo_dice_y_no_guarda_nada()
    {
        var video = CreateVideo();
        var itemId = Guid.NewGuid();
        Probe(Ok(ProbeJson));
        Ffmpeg(Failed());
        var service = CreateService();

        Assert.Equal(SubtitleStatus.Failed, (await service.GetVttAsync(itemId, video, "e2", CancellationToken.None)).Status);

        Ffmpeg(ProcessResult.NotInstalled);
        Assert.Equal(SubtitleStatus.Failed, (await service.GetVttAsync(itemId, video, "e3", CancellationToken.None)).Status);
        Assert.Equal(SubtitleStatus.Failed, (await service.GetVttAsync(itemId, video, "e3", CancellationToken.None)).Status);

        Assert.False(Directory.Exists(Path.Combine(_data, "subtitles")));
        Assert.Single(_logger.Messages, m => m.Level == LogLevel.Warning && m.Text.Contains("No se encontró ffmpeg"));
    }

    [Fact]
    public async Task Una_salida_que_no_es_WebVTT_cuenta_como_fallo()
    {
        var video = CreateVideo();
        Probe(Ok(ProbeJson));
        Ffmpeg(Ok("basura"));

        var result = await CreateService().GetVttAsync(Guid.NewGuid(), video, "e2", CancellationToken.None);

        Assert.Equal(SubtitleStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Una_pista_que_no_existe_responde_no_encontrada_y_no_ejecuta_ffmpeg()
    {
        var video = CreateVideo();
        Probe(Ok(ProbeJson));
        var service = CreateService();

        foreach (var trackId in new[] { "e9", "x0", "", "../e2", "e2.vtt" })
        {
            Assert.Equal(SubtitleStatus.NotFound, (await service.GetVttAsync(Guid.NewGuid(), video, trackId, CancellationToken.None)).Status);
        }

        Assert.Equal(0, Calls("ffmpeg"));
    }

    [Fact]
    public async Task Cancelar_la_solicitud_propaga_la_cancelacion()
    {
        var video = CreateVideo();
        Probe(Ok(ProbeJson));
        _processes.Setup(p => p.RunAsync(It.Is<ProcessRequest>(r => r.FileName == "ffmpeg"), It.IsAny<CancellationToken>()))
            .Returns<ProcessRequest, CancellationToken>(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Ok(Vtt);
            });
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();

        var pending = service.GetVttAsync(Guid.NewGuid(), video, "e2", cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Respeta_el_limite_de_extracciones_simultaneas()
    {
        var video = CreateVideo();
        Probe(Ok(ProbeJson));
        var gate = new object();
        var running = 0;
        var maximum = 0;
        _processes.Setup(p => p.RunAsync(It.Is<ProcessRequest>(r => r.FileName == "ffmpeg"), It.IsAny<CancellationToken>()))
            .Returns<ProcessRequest, CancellationToken>(async (_, _) =>
            {
                lock (gate)
                {
                    running++;
                    maximum = Math.Max(maximum, running);
                }

                await Task.Delay(50);
                lock (gate)
                {
                    running--;
                }

                return Ok(Vtt);
            });
        var service = CreateService(new MediaToolsOptions { MaxConcurrentExtractions = 1 });

        await Task.WhenAll(
            service.GetVttAsync(Guid.NewGuid(), video, "e2", CancellationToken.None),
            service.GetVttAsync(Guid.NewGuid(), video, "e3", CancellationToken.None),
            service.GetVttAsync(Guid.NewGuid(), video, "e2", CancellationToken.None));

        Assert.Equal(1, maximum);
    }

    // ───────────────────────── archivos sueltos ─────────────────────────

    [Fact]
    public async Task Los_archivos_sueltos_se_listan_en_orden_con_idioma_y_forzados()
    {
        var video = CreateVideo();
        CreateVideo("02.mp4");
        WriteFile("01.es.srt", "x");
        WriteFile("01.spa.forced.srt", "x");
        WriteFile("01.en.vtt", "WEBVTT\n");
        WriteFile("02.es.srt", "x");
        WriteFile("01.txt", "no es un subtítulo");
        Probe(Ok("""{ "streams": [] }"""));

        var tracks = await CreateService().ListAsync(Guid.NewGuid(), video, CancellationToken.None);

        Assert.Equal(new[] { "x0", "x1", "x2" }, tracks.Select(t => t.Id).ToArray());
        Assert.Equal(new[] { "Inglés", "Español", "Español (forzados)" }, tracks.Select(t => t.Label).ToArray());
        Assert.Equal(new[] { "webvtt", "subrip", "subrip" }, tracks.Select(t => t.Codec).ToArray());
        Assert.All(tracks, t => Assert.Equal(SubtitleOrigin.External, t.Origin));
        Assert.True(tracks[2].IsForced);
    }

    [Fact]
    public async Task Convierte_un_srt_suelto_a_WebVTT_sin_ffmpeg()
    {
        var video = CreateVideo();
        File.WriteAllText(
            Path.Combine(_media, "01.es.srt"),
            "1\r\n00:00:01,500 --> 00:00:03,000\r\nCanción\r\n",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Probe(ProcessResult.NotInstalled);

        var result = await CreateService().GetVttAsync(Guid.NewGuid(), video, "x0", CancellationToken.None);

        Assert.Equal(SubtitleStatus.Ok, result.Status);
        Assert.Equal("WEBVTT\n\n00:00:01.500 --> 00:00:03.000\nCanción\n\n", result.Content);
        Assert.Equal(0, Calls("ffmpeg"));
    }

    [Fact]
    public async Task Un_vtt_suelto_sale_tal_cual()
    {
        var video = CreateVideo();
        const string content = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000 line:0\n<v Ana>Hola</v>\n";
        WriteFile("01.vtt", content);
        Probe(ProcessResult.NotInstalled);

        var result = await CreateService().GetVttAsync(Guid.NewGuid(), video, "x0", CancellationToken.None);

        Assert.Equal(content, result.Content);
    }

    [Fact]
    public async Task Un_ass_suelto_usa_ffmpeg_si_esta_y_si_no_el_convertidor_de_C_sharp()
    {
        var video = CreateVideo();
        WriteFile("01.ass", "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\i1}Hola\n");
        Probe(ProcessResult.NotInstalled);
        Ffmpeg(ProcessResult.NotInstalled);

        var withoutFfmpeg = await CreateService().GetVttAsync(Guid.NewGuid(), video, "x0", CancellationToken.None);

        Assert.Equal("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHola\n\n", withoutFfmpeg.Content);

        // Con ffmpeg se usa lo que él entrega.
        Ffmpeg(Ok("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n<i>Hola</i>\n\n"));
        var service = CreateService();
        var withFfmpeg = await service.GetVttAsync(Guid.NewGuid(), video, "x0", CancellationToken.None);

        Assert.Equal("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n<i>Hola</i>\n\n", withFfmpeg.Content);
    }

    [Fact]
    public async Task Un_suelto_fuera_de_las_carpetas_de_medios_no_se_lista_ni_se_sirve()
    {
        // La carpeta de medios del servicio es "media"; el video y su subtítulo están en otra carpeta.
        var elsewhere = Path.Combine(_root, "otra");
        Directory.CreateDirectory(elsewhere);
        var video = Path.Combine(elsewhere, "01.mp4");
        File.WriteAllBytes(video, []);
        File.WriteAllText(Path.Combine(elsewhere, "01.es.srt"), "1\n00:00:01,000 --> 00:00:02,000\nHola\n");
        Probe(Ok("""{ "streams": [] }"""));

        var service = CreateService();

        Assert.Empty(await service.ListAsync(Guid.NewGuid(), video, CancellationToken.None));
        Assert.Equal(SubtitleStatus.NotFound, (await service.GetVttAsync(Guid.NewGuid(), video, "x0", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Un_subtitulo_suelto_enorme_no_se_lee()
    {
        var video = CreateVideo();
        await using (var stream = File.Create(Path.Combine(_media, "01.es.srt")))
        {
            stream.SetLength(9 * 1024 * 1024);
        }

        Probe(Ok("""{ "streams": [] }"""));

        Assert.Empty(await CreateService().ListAsync(Guid.NewGuid(), video, CancellationToken.None));
    }

    // ───────────────────────── subidos desde el reproductor ─────────────────────────

    private const string Srt = "1\r\n00:00:01,500 --> 00:00:03,000\r\nHola, mundo\r\n";

    private static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task Un_srt_subido_se_guarda_en_WebVTT_y_se_lista_al_final_como_subido()
    {
        var video = CreateVideo();
        WriteFile("01.es.srt", Srt);
        Probe(Ok(ProbeJson));
        var service = CreateService();
        var itemId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var upload = await service.UploadAsync(itemId, video, userId, "Mi episodio.en.srt", Bytes(Srt), null, null, CancellationToken.None);

        Assert.Equal(SubtitleUploadStatus.Ok, upload.Status);
        var track = upload.Track!;
        Assert.Matches("^u[0-9a-f]{32}$", track.Id);
        Assert.Equal(SubtitleOrigin.Uploaded, track.Origin);
        Assert.Equal("en", track.Language);
        Assert.Equal("Mi episodio", track.Title);
        Assert.Equal("subrip", track.Codec);
        Assert.Equal(userId, track.UploadedBy);
        Assert.Equal("Inglés (subido)", track.Label);

        // Va después de las incrustadas y de los sueltos, y solo en este video.
        var tracks = await service.ListAsync(itemId, video, CancellationToken.None);
        Assert.Equal(new[] { "e2", "e3", "x0", track.Id }, tracks.Select(t => t.Id).ToArray());
        Assert.Equal(new[] { "Inglés", "Español (incrustados)", "Español (archivo)", "Inglés (subido)" }, tracks.Select(t => t.Label).ToArray());
        Assert.DoesNotContain(await service.ListAsync(Guid.NewGuid(), video, CancellationToken.None), t => t.Origin == SubtitleOrigin.Uploaded);

        // Se guarda fuera de la caché de extracciones y se sirve sin volver a convertir.
        Assert.StartsWith(Path.Combine(_data, "uploads", "subtitles", itemId.ToString()), track.FilePath);
        var vtt = await service.GetVttAsync(itemId, video, track.Id, CancellationToken.None);
        Assert.Equal(SubtitleStatus.Ok, vtt.Status);
        Assert.Equal("WEBVTT\n\n00:00:01.500 --> 00:00:03.000\nHola, mundo\n\n", vtt.Content);
    }

    [Theory]
    [InlineData("Episodio 01.es.forced.srt", null, "es", true)]
    [InlineData("Episodio 01.es.srt", "en", "en", false)]
    [InlineData("Episodio 01.es.srt", "", null, false)]
    [InlineData("Episodio 01.srt", "Español", "es", false)]
    [InlineData("Episodio 01.srt", "ru", "ru", false)]
    [InlineData("Episodio 01.srt", "<script>", null, false)]
    [InlineData("Ataque.de.Titanes.srt", null, null, false)]
    public async Task El_idioma_sale_del_nombre_si_no_se_elige(string archivo, string? elegido, string? idioma, bool forzados)
    {
        var video = CreateVideo();
        Probe(Ok("""{ "streams": [] }"""));

        var upload = await CreateService().UploadAsync(
            Guid.NewGuid(), video, Guid.NewGuid(), archivo, Bytes(Srt), elegido, null, CancellationToken.None);

        Assert.Equal(SubtitleUploadStatus.Ok, upload.Status);
        Assert.Equal(idioma, upload.Track!.Language);
        Assert.Equal(forzados, upload.Track.IsForced);
    }

    [Fact]
    public async Task Sin_idioma_la_pista_subida_se_llama_como_su_archivo()
    {
        var video = CreateVideo();
        Probe(Ok("""{ "streams": [] }"""));

        var upload = await CreateService().UploadAsync(
            Guid.NewGuid(), video, Guid.NewGuid(), "C:\\fakepath\\Yani Neko 01.srt", Bytes(Srt), string.Empty, null, CancellationToken.None);

        Assert.Equal("Yani Neko 01 (subido)", upload.Track!.Label);
    }

    [Fact]
    public async Task Un_ass_subido_pasa_por_ffmpeg_y_si_no_esta_por_el_convertidor_de_CSharp()
    {
        const string Ass = "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Del ASS\n";
        var video = CreateVideo();
        Probe(Ok("""{ "streams": [] }"""));
        ProcessRequest? request = null;
        _processes.Setup(p => p.RunAsync(It.Is<ProcessRequest>(r => r.FileName == "ffmpeg"), It.IsAny<CancellationToken>()))
            .Callback<ProcessRequest, CancellationToken>((r, _) => request = r)
            .ReturnsAsync(Ok(Vtt));
        var service = CreateService();
        var itemId = Guid.NewGuid();

        var withFfmpeg = await service.UploadAsync(itemId, video, Guid.NewGuid(), "01.ja.ass", Bytes(Ass), null, null, CancellationToken.None);

        Assert.Equal(SubtitleUploadStatus.Ok, withFfmpeg.Status);
        Assert.Equal(Vtt, (await service.GetVttAsync(itemId, video, withFfmpeg.Track!.Id, CancellationToken.None)).Content);
        // ffmpeg leyó una copia temporal, que ya no existe.
        var input = request!.Arguments[request.Arguments.ToList().IndexOf("-i") + 1];
        Assert.EndsWith(".ass", input);
        Assert.False(File.Exists(input));

        Ffmpeg(ProcessResult.NotInstalled);
        var withoutFfmpeg = await service.UploadAsync(itemId, video, Guid.NewGuid(), "01.ja.ass", Bytes(Ass), null, null, CancellationToken.None);

        Assert.Equal(
            "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nDel ASS\n\n",
            (await service.GetVttAsync(itemId, video, withoutFfmpeg.Track!.Id, CancellationToken.None)).Content);
    }

    [Theory]
    [InlineData("foto.jpg", "1\n00:00:01,000 --> 00:00:02,000\nHola\n")]
    [InlineData("01.srt", "esto no es un subtítulo")]
    [InlineData("01.vtt", "00:00:01.000 --> 00:00:02.000\nSin cabecera\n")]
    [InlineData("01.srt", "")]
    [InlineData("", "1\n00:00:01,000 --> 00:00:02,000\nHola\n")]
    public async Task Rechaza_lo_que_no_es_un_subtitulo_y_no_guarda_nada(string archivo, string contenido)
    {
        var video = CreateVideo();
        var service = CreateService();

        var upload = await service.UploadAsync(Guid.NewGuid(), video, Guid.NewGuid(), archivo, Bytes(contenido), null, null, CancellationToken.None);

        Assert.Equal(SubtitleUploadStatus.Invalid, upload.Status);
        Assert.False(string.IsNullOrWhiteSpace(upload.Error));
        Assert.False(Directory.Exists(service.UploadsFolder) && Directory.EnumerateFiles(service.UploadsFolder, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Rechaza_lo_que_pesa_demasiado_y_lo_que_no_es_un_video()
    {
        var video = CreateVideo();
        var audio = CreateVideo("01.mp3");
        var service = CreateService();
        var tooBig = new byte[SubtitleService.MaxUploadBytes + 1];

        Assert.Equal(
            SubtitleUploadStatus.TooLarge,
            (await service.UploadAsync(Guid.NewGuid(), video, Guid.NewGuid(), "01.srt", tooBig, null, null, CancellationToken.None)).Status);
        Assert.Equal(
            SubtitleUploadStatus.NotVideo,
            (await service.UploadAsync(Guid.NewGuid(), audio, Guid.NewGuid(), "01.srt", Bytes(Srt), null, null, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task No_pasa_del_limite_de_subidos_por_video()
    {
        var video = CreateVideo();
        Probe(Ok("""{ "streams": [] }"""));
        var service = CreateService();
        var itemId = Guid.NewGuid();
        for (var i = 0; i < SubtitleService.MaxUploadsPerItem; i++)
        {
            Assert.Equal(
                SubtitleUploadStatus.Ok,
                (await service.UploadAsync(itemId, video, Guid.NewGuid(), $"{i}.srt", Bytes(Srt), null, null, CancellationToken.None)).Status);
        }

        var extra = await service.UploadAsync(itemId, video, Guid.NewGuid(), "otro.srt", Bytes(Srt), null, null, CancellationToken.None);
        var elsewhere = await service.UploadAsync(Guid.NewGuid(), video, Guid.NewGuid(), "otro.srt", Bytes(Srt), null, null, CancellationToken.None);

        Assert.Equal(SubtitleUploadStatus.TooMany, extra.Status);
        Assert.Equal(SubtitleUploadStatus.Ok, elsewhere.Status);
        Assert.Equal(SubtitleService.MaxUploadsPerItem, (await service.ListAsync(itemId, video, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Solo_quien_lo_subio_o_un_administrador_lo_borra()
    {
        var video = CreateVideo();
        WriteFile("01.es.srt", Srt);
        Probe(Ok("""{ "streams": [] }"""));
        var service = CreateService();
        var itemId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var first = (await service.UploadAsync(itemId, video, owner, "a.srt", Bytes(Srt), "es", null, CancellationToken.None)).Track!;
        var second = (await service.UploadAsync(itemId, video, owner, "b.srt", Bytes(Srt), "es", null, CancellationToken.None)).Track!;

        Assert.Equal(SubtitleDeleteStatus.Forbidden, await service.DeleteUploadAsync(itemId, first.Id, Guid.NewGuid(), false, CancellationToken.None));
        Assert.Equal(SubtitleDeleteStatus.Deleted, await service.DeleteUploadAsync(itemId, first.Id, owner, false, CancellationToken.None));
        Assert.Equal(SubtitleDeleteStatus.Deleted, await service.DeleteUploadAsync(itemId, second.Id, Guid.NewGuid(), true, CancellationToken.None));

        // Ya no están, y las pistas del archivo no se borran desde aquí.
        foreach (var trackId in new[] { first.Id, second.Id, "x0", "e2", "u", "U" + new string('a', 32), "../../x" })
        {
            Assert.Equal(SubtitleDeleteStatus.NotFound, await service.DeleteUploadAsync(itemId, trackId, owner, true, CancellationToken.None));
        }

        Assert.Equal(new[] { "x0" }, (await service.ListAsync(itemId, video, CancellationToken.None)).Select(t => t.Id).ToArray());
        Assert.Equal(SubtitleStatus.NotFound, (await service.GetVttAsync(itemId, video, first.Id, CancellationToken.None)).Status);
        Assert.False(Directory.Exists(Path.Combine(service.UploadsFolder, itemId.ToString())));
        Assert.True(File.Exists(Path.Combine(_media, "01.es.srt")));
    }

    /// <summary>Guarda lo que se escribe en el log para poder revisarlo.</summary>
    private sealed class CapturingLogger : ILogger<SubtitleService>
    {
        private readonly List<(LogLevel Level, string Text)> _messages = [];

        public IReadOnlyList<(LogLevel Level, string Text)> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
            {
                _messages.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
