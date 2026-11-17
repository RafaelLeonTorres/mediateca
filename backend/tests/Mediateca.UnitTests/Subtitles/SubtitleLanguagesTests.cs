using Mediateca.Core.Subtitles;
using Xunit;

namespace Mediateca.UnitTests.Subtitles;

public class SubtitleLanguagesTests
{
    [Theory]
    [InlineData("es", "es")]
    [InlineData("spa", "es")]
    [InlineData("SPA", "es")]
    [InlineData("Spanish", "es")]
    [InlineData("español", "es")]
    [InlineData("Español", "es")]
    [InlineData("castellano", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("en", "en")]
    [InlineData("eng", "en")]
    [InlineData("English", "en")]
    [InlineData("Inglés", "en")]
    [InlineData("ja", "ja")]
    [InlineData("jpn", "ja")]
    [InlineData("japonés", "ja")]
    [InlineData("pt", "pt")]
    [InlineData("por", "pt")]
    [InlineData("pt_BR", "pt")]
    [InlineData("Portuguese", "pt")]
    [InlineData("fre", "fr")]
    [InlineData("fra", "fr")]
    [InlineData("francés", "fr")]
    [InlineData("ger", "de")]
    [InlineData("deu", "de")]
    [InlineData("alemán", "de")]
    [InlineData("ita", "it")]
    [InlineData("Italian", "it")]
    [InlineData("chi", "zh")]
    [InlineData("zho", "zh")]
    [InlineData("zh-Hans", "zh")]
    [InlineData("chino", "zh")]
    [InlineData("kor", "ko")]
    [InlineData("coreano", "ko")]
    [InlineData("  ENG  ", "en")]
    public void Reconoce_los_idiomas_por_codigo_o_nombre(string texto, string esperado)
    {
        Assert.Equal(esperado, SubtitleLanguages.Normalize(texto));
        Assert.Equal(esperado, SubtitleLanguages.TryGetKnown(texto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("und")]
    [InlineData("UND")]
    [InlineData("zxx")]
    public void Sin_idioma_da_null(string? texto) => Assert.Null(SubtitleLanguages.Normalize(texto));

    [Fact]
    public void Un_idioma_desconocido_se_conserva_en_minusculas_pero_no_cuenta_como_conocido()
    {
        Assert.Equal("rus", SubtitleLanguages.Normalize("RUS"));
        Assert.Null(SubtitleLanguages.TryGetKnown("rus"));
        Assert.Null(SubtitleLanguages.TryGetKnown("Comentarios"));
    }

    [Theory]
    [InlineData("es", "Español")]
    [InlineData("en", "Inglés")]
    [InlineData("ja", "Japonés")]
    [InlineData("pt", "Portugués")]
    [InlineData("fr", "Francés")]
    [InlineData("de", "Alemán")]
    [InlineData("it", "Italiano")]
    [InlineData("zh", "Chino")]
    [InlineData("ko", "Coreano")]
    [InlineData("rus", "RUS")]
    public void Da_el_nombre_en_espanol(string codigo, string esperado) =>
        Assert.Equal(esperado, SubtitleLanguages.NameOf(codigo));
}
