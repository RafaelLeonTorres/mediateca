using Mediateca.Core.Metadata;

namespace Mediateca.UnitTests.Metadata;

/// <summary>Limpieza de los nombres reales de las carpetas de anime (2026) antes de buscarlos.</summary>
public class SearchNameCleanerTests
{
    [Theory]
    [InlineData("Aishiteru Game o Owarasetai", "aishiteru game o owarasetai", null, null, null, false)]
    [InlineData("Sousou no Frieren season 2", "sousou no frieren", 2, null, null, false)]
    // Los errores de tipeo de las carpetas: "Seasson" con dos eses.
    [InlineData("Fire Force Seasson 3", "fire force", 3, null, null, false)]
    [InlineData("Tensei Shitara Ken Deshita seasson 2", "tensei shitara ken deshita", 2, null, null, false)]
    [InlineData("Clevatess Majuu no Ou to Akago to Shikabane no Yuusha second seasson", "clevatess majuu no ou to akago to shikabane no yuusha", 2, null, null, false)]
    [InlineData("Oshi No Ko Season 3", "oshi no ko", 3, null, null, false)]
    [InlineData("Tensei Kizoku, Kantei Skill de Nariagaru Temporada 3", "tensei kizoku kantei skill de nariagaru", 3, null, null, false)]
    [InlineData("Trigun Stargaze S2", "trigun stargaze", 2, null, null, false)]
    [InlineData("ReZero kara Hajimeru Isekai Seikatsu 4th Season", "rezero kara hajimeru isekai seikatsu", 4, null, null, false)]
    // Cour y parte van aparte de la temporada.
    [InlineData("Hokuto no Ken Kenougun Zako-tachi no Banka 2nd Cour", "hokuto no ken kenougun zako tachi no banka", null, 2, null, false)]
    [InlineData("Mahou no Shimai Lulutto Lilly Cour 2", "mahou no shimai lulutto lilly", null, 2, null, false)]
    // Números romanos, también el carácter Ⅲ de un solo símbolo.
    [InlineData("Youjo Senki II", "youjo senki", 2, null, null, false)]
    [InlineData("Mushoku Tensei Ⅲ Isekai Ittara Honki Dasu", "mushoku tensei isekai ittara honki dasu", 3, null, null, false)]
    // El año de la carpeta, entre paréntesis o suelto al final.
    [InlineData("Boku no Hero Academia (2026)", "boku no hero academia", null, null, 2026, false)]
    [InlineData("Tsue to Tsurugi no Wistoria 2026", "tsue to tsurugi no wistoria", null, null, 2026, false)]
    // Películas.
    [InlineData("Gintama the Movie 2026 Yoshiwara in Flames", "gintama yoshiwara in flames", null, null, 2026, true)]
    [InlineData("Girls Band Cry Movie 01 Seishun Kyousoukyoku", "girls band cry seishun kyousoukyoku", null, null, null, true)]
    [InlineData("tensei shitara slime datta ken movie 2 soukai no namida-hen", "tensei shitara slime datta ken soukai no namida hen", null, null, null, true)]
    // Apóstrofes tipográficos y signos.
    [InlineData("JoJo’s Bizarre Adventure STEEL BALL RUN", "jojos bizarre adventure steel ball run", null, null, null, false)]
    // Letras matemáticas en negrita.
    [InlineData("𝐔𝐫𝐮𝐰𝐚𝐬𝐡𝐢 𝐧𝐨 𝐘𝐨𝐢 𝐧𝐨 𝐓𝐬𝐮𝐤𝐢", "uruwashi no yoi no tsuki", null, null, null, false)]
    // Una "о" cirílica (U+043E) dentro de un nombre latino.
    [InlineData("Juuоu Mujin Dandivine", "juuou mujin dandivine", null, null, null, false)]
    public void Limpia_el_nombre_de_la_carpeta(
        string carpeta, string titulo, int? temporada, int? parte, int? año, bool pelicula)
    {
        var pista = SearchNameCleaner.Clean(carpeta);

        Assert.Equal(titulo, pista.Title);
        Assert.Equal(temporada, pista.Season);
        Assert.Equal(parte, pista.Part);
        Assert.Equal(año, pista.Year);
        Assert.Equal(pelicula, pista.IsMovie);
    }

    [Fact]
    public void El_titulo_en_ingles_entre_parentesis_es_un_nombre_alterno()
    {
        var pista = SearchNameCleaner.Clean("Koori no Jouheki 2nd Season (The Ramparts of Ice Season 2)");

        Assert.Equal("koori no jouheki", pista.Title);
        Assert.Equal(new[] { "the ramparts of ice" }, pista.Alternates.ToArray());
        Assert.Equal(2, pista.Season);
    }

    [Fact]
    public void Un_parentesis_sin_cerrar_tambien_se_entiende()
    {
        var pista = SearchNameCleaner.Clean(
            "Mairimashita! Iruma-kun 4th Season (Welcome to Demon School! Iruma-kun Season 4");

        Assert.Equal("mairimashita iruma kun", pista.Title);
        Assert.Equal(new[] { "welcome to demon school iruma kun" }, pista.Alternates.ToArray());
        Assert.Equal(4, pista.Season);
    }

    [Fact]
    public void Todos_los_nombres_incluyen_el_principal_y_los_alternos()
    {
        var pista = SearchNameCleaner.Clean("Koori no Jouheki (The Ramparts of Ice)");

        Assert.Equal(new[] { "koori no jouheki", "the ramparts of ice" }, pista.AllTitles.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_nombre_vacio_da_un_titulo_vacio_sin_fallar(string? carpeta)
    {
        var pista = SearchNameCleaner.Clean(carpeta);

        Assert.Equal("", pista.Title);
        Assert.Empty(pista.Alternates);
    }
}
