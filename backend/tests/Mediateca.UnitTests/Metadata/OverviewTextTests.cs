using Mediateca.Core.Metadata;

namespace Mediateca.UnitTests.Metadata;

public class OverviewTextTests
{
    [Fact]
    public void Los_saltos_de_linea_html_pasan_a_saltos_de_linea()
    {
        var texto = OverviewText.FromHtml("Una maga elfa.<br><br>Viaja con <i>sus amigos</i>.<br />Fin.");

        Assert.Equal("Una maga elfa.\n\nViaja con sus amigos.\nFin.", texto);
    }

    [Fact]
    public void Quita_la_linea_final_de_la_fuente()
    {
        var texto = OverviewText.FromHtml("Una historia.<br><br>(Source: Crunchyroll)");

        Assert.Equal("Una historia.", texto);
    }

    [Fact]
    public void Decodifica_las_entidades_html()
    {
        var texto = OverviewText.FromHtml("&quot;Hola&quot; &amp; adiós &#8211; &lt;fin&gt;");

        Assert.Equal("\"Hola\" & adiós – <fin>", texto);
    }

    [Fact]
    public void Los_parrafos_quedan_separados_por_una_linea_en_blanco()
    {
        Assert.Equal("uno\n\ndos", OverviewText.FromHtml("<p>uno</p><p>dos</p>"));
    }

    [Fact]
    public void Junta_los_espacios_y_los_saltos_de_mas()
    {
        Assert.Equal("a b\n\nc", OverviewText.FromHtml("a    b<br><br><br><br>   c  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<br><i></i>")]
    [InlineData("(Source: AniList)")]
    public void Sin_texto_devuelve_null(string? html)
    {
        Assert.Null(OverviewText.FromHtml(html));
    }
}
