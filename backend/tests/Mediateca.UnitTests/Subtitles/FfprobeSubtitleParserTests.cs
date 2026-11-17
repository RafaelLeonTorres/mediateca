using Mediateca.Core.Subtitles;
using Xunit;

namespace Mediateca.UnitTests.Subtitles;

public class FfprobeSubtitleParserTests
{
    /// <summary>Salida de ffprobe de un MP4 de la biblioteca: mov_text en inglés y en español, uno de imagen y uno sin idioma.</summary>
    private const string Json = """
        {
          "streams": [
            {
              "index": 2,
              "codec_name": "mov_text",
              "codec_long_name": "MPEG-4 Timed Text",
              "codec_type": "subtitle",
              "disposition": { "default": 1, "forced": 0, "hearing_impaired": 0 },
              "tags": { "language": "eng", "handler_name": "SubtitleHandler" }
            },
            {
              "index": 3,
              "codec_name": "mov_text",
              "codec_type": "subtitle",
              "disposition": { "default": 0, "forced": 0 },
              "tags": { "language": "spa", "handler_name": "SubtitleHandler" }
            },
            {
              "index": 4,
              "codec_name": "hdmv_pgs_subtitle",
              "codec_type": "subtitle",
              "disposition": { "default": 0, "forced": 0 },
              "tags": { "language": "jpn" }
            },
            {
              "index": 5,
              "codec_name": "subrip",
              "codec_type": "subtitle",
              "disposition": { "default": 0, "forced": 1 },
              "tags": { "language": "und", "title": "Forced" }
            }
          ]
        }
        """;

    [Fact]
    public void Lee_las_pistas_de_texto_de_un_MP4_con_mov_text()
    {
        var tracks = FfprobeSubtitleParser.Parse(Json);

        Assert.Equal(new[] { "e2", "e3", "e5" }, tracks.Select(t => t.Id).ToArray());

        var english = tracks[0];
        Assert.Equal("en", english.Language);
        Assert.Equal("mov_text", english.Codec);
        Assert.Equal(SubtitleOrigin.Embedded, english.Origin);
        Assert.True(english.IsDefault);
        Assert.False(english.IsForced);
        Assert.Equal(2, english.StreamIndex);
        Assert.Null(english.FilePath);

        Assert.Equal("es", tracks[1].Language);
        Assert.False(tracks[1].IsDefault);
    }

    [Fact]
    public void No_lista_los_subtitulos_de_imagen()
    {
        var tracks = FfprobeSubtitleParser.Parse(Json);

        Assert.DoesNotContain(tracks, t => t.Codec == "hdmv_pgs_subtitle");
        Assert.DoesNotContain(tracks, t => t.Id == "e4");
    }

    [Fact]
    public void Una_pista_sin_idioma_y_forzada_por_su_disposicion_o_su_titulo()
    {
        var forced = FfprobeSubtitleParser.Parse(Json)[2];

        Assert.Null(forced.Language);
        Assert.True(forced.IsForced);
        Assert.Equal("Forced", forced.Title);

        const string byTitle = """{ "streams": [ { "index": 1, "codec_name": "ass", "codec_type": "subtitle", "tags": { "language": "spa", "title": "Forzados" } } ] }""";
        Assert.True(FfprobeSubtitleParser.Parse(byTitle)[0].IsForced);
    }

    [Theory]
    [InlineData("subrip", true)]
    [InlineData("ass", true)]
    [InlineData("ssa", true)]
    [InlineData("mov_text", true)]
    [InlineData("webvtt", true)]
    [InlineData("text", true)]
    [InlineData("MOV_TEXT", true)]
    [InlineData("hdmv_pgs_subtitle", false)]
    [InlineData("dvd_subtitle", false)]
    [InlineData("dvb_subtitle", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Reconoce_los_codecs_de_texto(string? codec, bool esperado) =>
        Assert.Equal(esperado, FfprobeSubtitleParser.IsTextCodec(codec));

    [Fact]
    public void Las_etiquetas_pueden_venir_en_mayusculas_como_en_los_MKV()
    {
        const string json = """{ "streams": [ { "index": 7, "codec_name": "ass", "codec_type": "subtitle", "tags": { "LANGUAGE": "jpn", "TITLE": "Honorífico" } } ] }""";

        var track = Assert.Single(FfprobeSubtitleParser.Parse(json));

        Assert.Equal("ja", track.Language);
        Assert.Equal("Honorífico", track.Title);
        Assert.Equal("e7", track.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es json")]
    [InlineData("{ \"streams\": ")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{ \"streams\": {} }")]
    [InlineData("{ \"streams\": [ 1, \"a\", { \"codec_name\": \"subrip\" } ] }")]
    public void Una_salida_vacia_o_danada_da_una_lista_vacia(string? json) =>
        Assert.Empty(FfprobeSubtitleParser.Parse(json));
}
