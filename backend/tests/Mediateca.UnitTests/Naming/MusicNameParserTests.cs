using Mediateca.Core.Naming;
using Xunit;

namespace Mediateca.UnitTests.Naming;

public class MusicNameParserTests
{
    [Theory]
    // Ejemplo de PLAN.md.
    [InlineData("Artista/Álbum (2020)/01 - Canción.flac", "Artista", "Álbum", 2020, null, 1, "Canción")]
    [InlineData("Artista\\Álbum (2020)\\01 - Canción.flac", "Artista", "Álbum", 2020, null, 1, "Canción")]
    // Disco y pista en el nombre del archivo.
    [InlineData("Artista/Album/1-03 - Cancion.mp3", "Artista", "Album", null, 1, 3, "Cancion")]
    // Carpetas de disco.
    [InlineData("Artista/Album (2001)/CD1/05. Tema.flac", "Artista", "Album", 2001, 1, 5, "Tema")]
    [InlineData("Artista/Album/Disc 2/2-04 Tema.flac", "Artista", "Album", null, 2, 4, "Tema")]
    // Otras formas de escribir el año del álbum.
    [InlineData("Artista/[2019] Album/03_Tema_Uno.mp3", "Artista", "Album", 2019, null, 3, "Tema Uno")]
    [InlineData("Artista/2018 - Album/07 Tema.m4a", "Artista", "Album", 2018, null, 7, "Tema")]
    // Los corchetes de formato no son parte del álbum.
    [InlineData("Artista/Álbum [FLAC] (2020)/101 - Tema.flac", "Artista", "Álbum", 2020, null, 101, "Tema")]
    // Menos carpetas.
    [InlineData("Album/01 - Tema.mp3", null, "Album", null, null, 1, "Tema")]
    [InlineData("01 - Tema.mp3", null, null, null, null, 1, "Tema")]
    // Sin número de pista, o con un número que es parte del título.
    [InlineData("Artista/Album/Sin numero.mp3", "Artista", "Album", null, null, null, "Sin numero")]
    [InlineData("Artista/Album/1979.mp3", "Artista", "Album", null, null, null, "1979")]
    [InlineData("Artista/Album/7 Years.mp3", "Artista", "Album", null, null, null, "7 Years")]
    public void Lee_artista_album_año_disco_pista_y_titulo(
        string ruta, string? artista, string? album, int? año, int? disco, int? pista, string titulo)
    {
        var resultado = MusicNameParser.Parse(ruta);

        Assert.Equal(artista, resultado.Artist);
        Assert.Equal(album, resultado.Album);
        Assert.Equal(año, resultado.Year);
        Assert.Equal(disco, resultado.DiscNumber);
        Assert.Equal(pista, resultado.TrackNumber);
        Assert.Equal(titulo, resultado.Title);
    }

    [Theory]
    [InlineData("CD1", true, 1)]
    [InlineData("Disc 2", true, 2)]
    [InlineData("disco 3", true, 3)]
    [InlineData("Album (2020)", false, 0)]
    [InlineData("Discovery", false, 0)]
    public void Reconoce_las_carpetas_de_disco(string carpeta, bool esDisco, int numero)
    {
        Assert.Equal(esDisco, MusicNameParser.IsDiscFolder(carpeta, out var disco));
        Assert.Equal(numero, disco);
    }
}
