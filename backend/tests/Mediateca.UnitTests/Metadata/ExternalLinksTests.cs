using Mediateca.Core.Media;
using Mediateca.Core.Metadata;

namespace Mediateca.UnitTests.Metadata;

public class ExternalLinksTests
{
    [Fact]
    public void Sin_enlaces_no_se_guarda_nada()
    {
        Assert.Null(ExternalLinks.Serialize([]));
    }

    [Fact]
    public void Los_enlaces_hacen_el_viaje_de_ida_y_vuelta()
    {
        var json = ExternalLinks.Serialize([
            new ExternalLink("AniList", "https://anilist.co/anime/21"),
            new ExternalLink("IMDb", "https://www.imdb.com/title/tt0118799/"),
        ]);

        var enlaces = ExternalLinks.Parse(json);

        Assert.Equal(2, enlaces.Count);
        Assert.Equal(new ExternalLink("AniList", "https://anilist.co/anime/21"), enlaces[0]);
        Assert.Contains("\"site\"", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("{\"site\":\"AniList\"}")]
    public void Un_json_vacio_o_danado_da_una_lista_vacia_sin_fallar(string? json)
    {
        Assert.Empty(ExternalLinks.Parse(json));
    }

    [Fact]
    public void Los_enlaces_sin_sitio_o_sin_direccion_se_descartan()
    {
        var enlaces = ExternalLinks.Parse("""[{"site":"","url":"https://a.test"},{"site":"A","url":""},{"site":"B","url":"https://b.test"}]""");

        Assert.Single(enlaces);
        Assert.Equal("B", enlaces[0].Site);
    }

    [Fact]
    public void La_clave_del_escaner_distingue_el_ano_y_no_las_mayusculas()
    {
        Assert.Equal(ScanKeys.Series("Mi Anime", 2019), ScanKeys.Series("MI ANIME", 2019));
        Assert.NotEqual(ScanKeys.Series("Mi Anime", 2019), ScanKeys.Series("Mi Anime", null));
    }
}
