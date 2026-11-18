using Mediateca.Core.Naming;
using Xunit;

namespace Mediateca.UnitTests.Naming;

public class MovieNameParserTests
{
    [Theory]
    // Ejemplo de PLAN.md: carpeta y archivo con el mismo nombre.
    [InlineData("Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4", "Big Buck Bunny", 2008)]
    [InlineData("Big Buck Bunny (2008).mp4", "Big Buck Bunny", 2008)]
    [InlineData("Big Buck Bunny (2008)\\Big Buck Bunny (2008).mp4", "Big Buck Bunny", 2008)]
    // Puntos y guiones bajos como separadores.
    [InlineData("Big_Buck_Bunny_(2008).mp4", "Big Buck Bunny", 2008)]
    [InlineData("The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv", "The Matrix", 1999)]
    [InlineData("Amelie.2001.720p.WEB-DL.H.264.mkv", "Amelie", 2001)]
    [InlineData("Dune.Part.Two.2024.HDR.2160p.WEB-DL.DDP5.1.Atmos.mkv", "Dune Part Two", 2024)]
    // Los puntos de un nombre con espacios son parte del título.
    [InlineData("Mr. Robot (2015).mp4", "Mr. Robot", 2015)]
    // Un número que forma parte del título no es el año.
    [InlineData("2012 (2009)/2012.mkv", "2012", 2009)]
    [InlineData("2001.A.Space.Odyssey.1968.2160p.UHD.BluRay.HEVC.mkv", "2001 A Space Odyssey", 1968)]
    [InlineData("Blade.Runner.2049.1080p.mkv", "Blade Runner 2049", null)]
    [InlineData("Blade Runner 2049 (2017)/movie.mkv", "Blade Runner 2049", 2017)]
    // La carpeta manda cuando el nombre del archivo es genérico o no trae el año.
    [InlineData("Matrix (1999)/matrix-hd.mkv", "Matrix", 1999)]
    [InlineData("Película Rara (2010) [1080p x265]/video.mp4", "Película Rara", 2010)]
    // Una carpeta sin año (una colección) no le gana al archivo que sí lo trae.
    [InlineData("Marvel/Iron Man (2008).mkv", "Iron Man", 2008)]
    public void Lee_el_titulo_y_el_año(string ruta, string titulo, int? año)
    {
        var resultado = MovieNameParser.Parse(ruta);

        Assert.Equal(titulo, resultado.Title);
        Assert.Equal(año, resultado.Year);
    }

    [Theory]
    [InlineData("Avatar [imdbid-tt0499549] (2009)/avatar.mp4", "Avatar", 2009, "tt0499549")]
    [InlineData("Avatar (2009) [imdbid-tt0499549].mkv", "Avatar", 2009, "tt0499549")]
    [InlineData("Heat [IMDBID-TT0113277] (1995)/heat.mp4", "Heat", 1995, "tt0113277")]
    public void Quita_la_etiqueta_de_IMDb_del_titulo(string ruta, string titulo, int? año, string imdb)
    {
        var resultado = MovieNameParser.Parse(ruta);

        Assert.Equal(titulo, resultado.Title);
        Assert.Equal(año, resultado.Year);
        Assert.Equal(imdb, resultado.ImdbId);
        Assert.Null(resultado.AniListId);
    }

    [Fact]
    public void Sin_etiquetas_no_hay_identificadores()
    {
        var resultado = MovieNameParser.Parse("Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4");

        Assert.Null(resultado.ImdbId);
        Assert.Null(resultado.AniListId);
    }

    [Fact]
    public void Una_ruta_vacia_es_un_error()
    {
        Assert.Throws<ArgumentException>(() => MovieNameParser.Parse(" "));
    }
}
