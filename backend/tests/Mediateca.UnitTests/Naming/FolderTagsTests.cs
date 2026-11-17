using Mediateca.Core.Naming;
using Xunit;

namespace Mediateca.UnitTests.Naming;

public class FolderTagsTests
{
    [Theory]
    [InlineData("Avatar [imdbid-tt0499549] (2009)", "Avatar (2009)", "tt0499549", null)]
    [InlineData("Heat {imdb-tt0113277}", "Heat", "tt0113277", null)]
    [InlineData("Mi Anime [anidbid-23]", "Mi Anime", null, 23)]
    [InlineData("Mi Anime (2019) [ANIDBID-4567]", "Mi Anime (2019)", null, 4567)]
    [InlineData("Sin etiquetas (2000)", "Sin etiquetas (2000)", null, null)]
    [InlineData("Con [Grupo] otra etiqueta", "Con [Grupo] otra etiqueta", null, null)]
    public void Quita_las_etiquetas_y_devuelve_los_identificadores(
        string nombre, string limpio, string? imdb, int? anidb)
    {
        var resultado = FolderTags.Extract(nombre);

        Assert.Equal(limpio, resultado.Name);
        Assert.Equal(imdb, resultado.ImdbId);
        Assert.Equal(anidb, resultado.AniDbId);
    }
}
