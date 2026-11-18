using Mediateca.Core.Naming;
using Xunit;

namespace Mediateca.UnitTests.Naming;

public class FolderTagsTests
{
    [Theory]
    [InlineData("Avatar [imdbid-tt0499549] (2009)", "Avatar (2009)", "tt0499549", null)]
    [InlineData("Heat {imdb-tt0113277}", "Heat", "tt0113277", null)]
    [InlineData("Mi Anime [anilistid-21]", "Mi Anime", null, 21)]
    [InlineData("Mi Anime (2019) [ANILISTID-154587]", "Mi Anime (2019)", null, 154587)]
    [InlineData("Mi Anime {anilist-21}", "Mi Anime", null, 21)]
    [InlineData("Sin etiquetas (2000)", "Sin etiquetas (2000)", null, null)]
    [InlineData("Con [Grupo] otra etiqueta", "Con [Grupo] otra etiqueta", null, null)]
    public void Quita_las_etiquetas_y_devuelve_los_identificadores(
        string nombre, string limpio, string? imdb, int? anilist)
    {
        var resultado = FolderTags.Extract(nombre);

        Assert.Equal(limpio, resultado.Name);
        Assert.Equal(imdb, resultado.ImdbId);
        Assert.Equal(anilist, resultado.AniListId);
    }

    [Theory]
    [InlineData("Mi Anime [anidbid-23]", "Mi Anime")]
    [InlineData("Mi Anime (2019) [ANIDBID-4567]", "Mi Anime (2019)")]
    public void La_etiqueta_de_AniDB_se_quita_pero_su_id_se_descarta(string nombre, string limpio)
    {
        // Un id de AniDB no sirve en AniList: la etiqueta de la fase 2 solo deja de estorbar en el nombre.
        var resultado = FolderTags.Extract(nombre);

        Assert.Equal(limpio, resultado.Name);
        Assert.Null(resultado.AniListId);
        Assert.Null(resultado.ImdbId);
    }
}
