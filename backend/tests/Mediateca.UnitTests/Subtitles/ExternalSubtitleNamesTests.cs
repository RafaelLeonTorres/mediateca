using Mediateca.Core.Subtitles;
using Xunit;

namespace Mediateca.UnitTests.Subtitles;

public class ExternalSubtitleNamesTests
{
    [Fact]
    public void Un_archivo_con_el_mismo_nombre_no_trae_idioma()
    {
        Assert.Equal(new ExternalSubtitleName(null, false, false, null), ExternalSubtitleNames.Parse("01.mp4", "01.srt"));
    }

    [Theory]
    [InlineData("01.es.srt", "es", false)]
    [InlineData("01.spa.srt", "es", false)]
    [InlineData("01.spa.forced.srt", "es", true)]
    [InlineData("01.Spanish.ass", "es", false)]
    [InlineData("01.SPA.Forzados.vtt", "es", true)]
    [InlineData("01.forced.es.ssa", "es", true)]
    [InlineData("01.en.srt", "en", false)]
    [InlineData("01.eng.SRT", "en", false)]
    [InlineData("01.pt-BR.srt", "pt", false)]
    [InlineData("01.ja.srt", "ja", false)]
    public void Saca_el_idioma_y_los_forzados_de_los_segmentos_de_en_medio(string archivo, string idioma, bool forzado)
    {
        var name = ExternalSubtitleNames.Parse("01.mp4", archivo);

        Assert.NotNull(name);
        Assert.Equal(idioma, name.Language);
        Assert.Equal(forzado, name.IsForced);
        Assert.Null(name.Title);
    }

    [Fact]
    public void Lo_que_no_es_idioma_ni_marca_queda_como_titulo()
    {
        var name = ExternalSubtitleNames.Parse("01.mp4", "01.es.Comentarios del director.srt");

        Assert.NotNull(name);
        Assert.Equal("es", name.Language);
        Assert.Equal("Comentarios del director", name.Title);
    }

    [Fact]
    public void Reconoce_la_marca_default()
    {
        var name = ExternalSubtitleNames.Parse("01.mp4", "01.en.default.srt");

        Assert.NotNull(name);
        Assert.True(name.IsDefault);
        Assert.Equal("en", name.Language);
    }

    [Theory]
    [InlineData("01.mp4", "010.srt")]
    [InlineData("01.mp4", "02.srt")]
    [InlineData("01.mp4", "01x.es.srt")]
    [InlineData("01.mp4", "01.mp4")]
    [InlineData("01.mp4", "01.txt")]
    [InlineData("01.mp4", "01.es.sub")]
    [InlineData("01.mp4", "01.es.idx")]
    [InlineData("01.mp4", "otro.srt")]
    [InlineData("Show.S01E01.mkv", "Show.S01E01E02.en.srt")]
    public void Lo_que_no_es_de_ese_video_no_cuenta(string video, string archivo) =>
        Assert.Null(ExternalSubtitleNames.Parse(video, archivo));

    [Fact]
    public void No_distingue_mayusculas_en_el_nombre_ni_en_la_extension()
    {
        var name = ExternalSubtitleNames.Parse("Episodio 01.MP4", "episodio 01.ES.SRT");

        Assert.NotNull(name);
        Assert.Equal("es", name.Language);
    }

    [Fact]
    public void Funciona_con_nombres_que_tienen_puntos()
    {
        var name = ExternalSubtitleNames.Parse("Show.S01E01.mkv", "Show.S01E01.en.srt");

        Assert.NotNull(name);
        Assert.Equal("en", name.Language);
    }

    [Theory]
    [InlineData("a.srt", true)]
    [InlineData("a.VTT", true)]
    [InlineData("a.ass", true)]
    [InlineData("a.SSA", true)]
    [InlineData("a.sub", false)]
    [InlineData("a.mp4", false)]
    [InlineData("a", false)]
    public void Reconoce_las_extensiones(string archivo, bool esperado) =>
        Assert.Equal(esperado, ExternalSubtitleNames.IsSubtitleFile(archivo));

    [Theory]
    [InlineData("a.srt", "subrip")]
    [InlineData("a.vtt", "webvtt")]
    [InlineData("a.ass", "ass")]
    [InlineData("a.SSA", "ssa")]
    public void Anuncia_el_codec_segun_la_extension(string archivo, string codec) =>
        Assert.Equal(codec, ExternalSubtitleNames.CodecOf(archivo));
}
