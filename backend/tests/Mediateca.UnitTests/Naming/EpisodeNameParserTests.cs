using Mediateca.Core.Naming;
using Xunit;

namespace Mediateca.UnitTests.Naming;

public class EpisodeNameParserTests
{
    [Theory]
    // Ejemplo de PLAN.md.
    [InlineData("Mi Serie (2019)/Temporada 01/Mi Serie S01E02.mkv", "Mi Serie", 2019, 1, 2, null)]
    [InlineData("Mi Serie (2019)\\Temporada 01\\Mi Serie S01E02.mkv", "Mi Serie", 2019, 1, 2, null)]
    // Formas de escribir temporada y episodio.
    [InlineData("Mi Serie (2019)/Season 2/Mi.Serie.s2e10.1080p.mkv", "Mi Serie", 2019, 2, 10, null)]
    [InlineData("Mi Serie (2019)/Temporada 01/Mi Serie 1x02 - El comienzo.mkv", "Mi Serie", 2019, 1, 2, "El comienzo")]
    [InlineData("Serie/Temporada 1/serie.s01e03.mkv", "Serie", null, 1, 3, null)]
    [InlineData("Mi.Serie.2019.S03E04.720p.HDTV.x264.mkv", "Mi Serie", 2019, 3, 4, null)]
    // Archivo suelto en la carpeta de la biblioteca: el título sale del nombre.
    [InlineData("Mi Serie S01E02.mkv", "Mi Serie", null, 1, 2, null)]
    [InlineData("Temporada 01/Mi Serie S01E02.mkv", "Mi Serie", null, 1, 2, null)]
    // Especiales.
    [InlineData("Mi Serie (2019)/Especiales/Mi Serie S00E01.mkv", "Mi Serie", 2019, 0, 1, null)]
    // Varios episodios en un archivo: se queda el primero.
    [InlineData("Mi Serie (2019)/Temporada 01/Mi Serie S01E01E02.mkv", "Mi Serie", 2019, 1, 1, null)]
    [InlineData("Mi Serie (2019)/Temporada 01/Mi Serie S01E01-E02.mkv", "Mi Serie", 2019, 1, 1, null)]
    [InlineData("Mi Serie (2019)/Temporada 01/Mi Serie S01E01-02 - Doble.mkv", "Mi Serie", 2019, 1, 1, "Doble")]
    // Con carpeta de temporada, el número suelto es el del episodio.
    [InlineData("Mi Serie/Temporada 02/Mi Serie - 05.mkv", "Mi Serie", null, 2, 5, null)]
    [InlineData("Mi Serie (2019) [imdbid-tt1234567]/Temporada 01/05 - Titulo.mkv", "Mi Serie", 2019, 1, 5, "Titulo")]
    // Una resolución no es temporada y episodio.
    [InlineData("Serie/Season 1/Serie 1920x1080 S01E01.mkv", "Serie", null, 1, 1, null)]
    // Lo que está entre corchetes no cuenta.
    [InlineData("Serie/Mi Serie S01E02 [1080p].mkv", "Serie", null, 1, 2, null)]
    public void Lee_series(string ruta, string serie, int? añoSerie, int? temporada, int? episodio, string? titulo)
    {
        var resultado = EpisodeNameParser.ParseSeries(ruta);

        Assert.Equal(serie, resultado.SeriesTitle);
        Assert.Equal(añoSerie, resultado.SeriesYear);
        Assert.Equal(temporada, resultado.SeasonNumber);
        Assert.Equal(episodio, resultado.EpisodeNumber);
        Assert.Null(resultado.AbsoluteNumber);
        Assert.Equal(titulo, resultado.EpisodeTitle);
    }

    [Theory]
    // Ejemplo de PLAN.md: el grupo y la calidad entre corchetes se ignoran.
    [InlineData("Mi Anime/[Grupo] Mi Anime - 05 [1080p].mkv", "Mi Anime", 5, null)]
    [InlineData("[Grupo] Mi Anime - 05 [1080p].mkv", "Mi Anime", 5, null)]
    // Episodios con versión.
    [InlineData("Mi Anime/[Grupo] Mi Anime - 05v2 [1080p].mkv", "Mi Anime", 5, null)]
    // Calidad entre paréntesis y CRC.
    [InlineData("Mi Anime (2019) [anidbid-23]/[Grupo] Mi Anime - 12 (1080p x265) [ABCD1234].mkv", "Mi Anime", 12, null)]
    // El año no es el episodio.
    [InlineData("Mi Anime/Mi Anime - 2019 - 05.mkv", "Mi Anime", 5, null)]
    [InlineData("Mi Anime/[G] Mi Anime - 05 (2019).mkv", "Mi Anime", 5, null)]
    // Una resolución no es el episodio, pero el episodio 1080 de un anime largo sí lo es.
    [InlineData("Mi Anime/[Grupo] Mi Anime - 1080p.mkv", "Mi Anime", null, null)]
    [InlineData("One Piece/[Grupo] One Piece - 1080 [1080p].mkv", "One Piece", 1080, null)]
    // Título del episodio después del número.
    [InlineData("Mi Anime/[Grupo] Mi Anime - 05 - Titulo del capitulo [1080p].mkv", "Mi Anime", 5, "Titulo del capitulo")]
    // Otras formas de escribir el número.
    [InlineData("Mi Anime/Mi Anime 07.mkv", "Mi Anime", 7, null)]
    [InlineData("Mi Anime/Mi_Anime_-_03_[720p].mkv", "Mi Anime", 3, null)]
    [InlineData("Mi Anime/Episode 12.mkv", "Mi Anime", 12, null)]
    [InlineData("Mi Anime/[Grupo] Mi Anime - 05-06 [1080p].mkv", "Mi Anime", 5, null)]
    // Si el título tiene un guion, el número es el último.
    [InlineData("Fate - Zero/[G] Fate - Zero - 05 [BD 1080p].mkv", "Fate - Zero", 5, null)]
    public void Lee_anime_con_numero_absoluto(string ruta, string serie, int? absoluto, string? titulo)
    {
        var resultado = EpisodeNameParser.ParseAnime(ruta);

        Assert.Equal(serie, resultado.SeriesTitle);
        Assert.Null(resultado.SeasonNumber);
        Assert.Null(resultado.EpisodeNumber);
        Assert.Equal(absoluto, resultado.AbsoluteNumber);
        Assert.Equal(titulo, resultado.EpisodeTitle);
    }

    [Theory]
    [InlineData("Mi Anime/[Grupo] Mi Anime - S02E03 [720p].mkv", 2, 3)]
    // En una carpeta de temporada, el número es el del episodio y no el absoluto.
    [InlineData("Mi Anime/Season 2/[G] Mi Anime - 05.mkv", 2, 5)]
    public void El_anime_tambien_acepta_temporada_y_episodio(string ruta, int temporada, int episodio)
    {
        var resultado = EpisodeNameParser.ParseAnime(ruta);

        Assert.Equal("Mi Anime", resultado.SeriesTitle);
        Assert.Equal(temporada, resultado.SeasonNumber);
        Assert.Equal(episodio, resultado.EpisodeNumber);
        Assert.Null(resultado.AbsoluteNumber);
    }

    [Theory]
    // Nombres que son solo el número, como los que deja una descarga por episodio: "01.mp4".
    [InlineData("Mi Anime/01.mp4", 1)]
    [InlineData("Mi Anime/12v2.mkv", 12)]
    [InlineData("Mi Anime/1080.mkv", 1080)]
    public void En_anime_un_nombre_que_es_solo_el_numero_es_el_episodio(string ruta, int absoluto)
    {
        var resultado = EpisodeNameParser.ParseAnime(ruta);

        Assert.Equal("Mi Anime", resultado.SeriesTitle);
        Assert.Equal(absoluto, resultado.AbsoluteNumber);
        Assert.Null(resultado.SeasonNumber);
        Assert.Null(resultado.EpisodeTitle);
        Assert.Null(resultado.Group);
    }

    [Fact]
    public void Un_anio_suelto_no_es_un_episodio()
    {
        var resultado = EpisodeNameParser.ParseAnime("Mi Anime/2026.mp4");

        Assert.Null(resultado.AbsoluteNumber);
        Assert.Null(resultado.EpisodeNumber);
    }

    [Theory]
    // Las carpetas dentro de la serie que no son de temporada (idioma, versión) se conservan tal cual.
    [InlineData("Mi Anime/Lat/01.mp4", "Lat", 1)]
    [InlineData("Mi Anime/sub/02.mp4", "sub", 2)]
    [InlineData("Mi Anime/Otra Serie/Lat/03.mp4", "Otra Serie / Lat", 3)]
    [InlineData("Mi Anime/Lat/[Grupo] Mi Anime - 04 [1080p].mkv", "Lat", 4)]
    public void Las_carpetas_dentro_de_la_serie_forman_un_grupo(string ruta, string grupo, int absoluto)
    {
        var resultado = EpisodeNameParser.ParseAnime(ruta);

        Assert.Equal("Mi Anime", resultado.SeriesTitle);
        Assert.Equal(grupo, resultado.Group);
        Assert.Equal(absoluto, resultado.AbsoluteNumber);
        Assert.Null(resultado.SeasonNumber);
    }

    [Fact]
    public void Una_carpeta_de_temporada_sola_no_es_un_grupo_pero_dentro_de_otra_carpeta_si()
    {
        var temporada = EpisodeNameParser.ParseSeries("Mi Serie (2019)/Temporada 01/Mi Serie S01E02.mkv");
        var dentroDeLat = EpisodeNameParser.ParseAnime("Mi Anime/Lat/Temporada 2/03.mp4");

        Assert.Null(temporada.Group);
        Assert.Equal(1, temporada.SeasonNumber);
        Assert.Equal("Lat / Temporada 2", dentroDeLat.Group);
        Assert.Equal(2, dentroDeLat.SeasonNumber);
        Assert.Equal(3, dentroDeLat.EpisodeNumber);
    }

    [Theory]
    [InlineData("Mi Anime/Especial01.mp4", 1)]
    [InlineData("Mi Anime/[Grupo] Mi Anime - SP02 [1080p].mkv", 2)]
    [InlineData("Mi Anime/Mi Anime OVA 3.mkv", 3)]
    [InlineData("Mi Anime/Lat/Special 4.mp4", 4)]
    public void Los_especiales_van_a_la_temporada_cero(string ruta, int episodio)
    {
        var resultado = EpisodeNameParser.ParseAnime(ruta);

        Assert.Equal("Mi Anime", resultado.SeriesTitle);
        Assert.Equal(0, resultado.SeasonNumber);
        Assert.Equal(episodio, resultado.EpisodeNumber);
        Assert.Null(resultado.AbsoluteNumber);
    }

    [Theory]
    // Una película dentro de la biblioteca de anime, con una versión por idioma: no hay número.
    [InlineData("Mi Pelicula/Lat.mp4")]
    [InlineData("Mi Pelicula/Mi Pelicula.mp4")]
    [InlineData("Spy Family/Spy Family.mkv")]
    public void Sin_numero_no_hay_episodio(string ruta)
    {
        var resultado = EpisodeNameParser.ParseAnime(ruta);

        Assert.Null(resultado.SeasonNumber);
        Assert.Null(resultado.EpisodeNumber);
        Assert.Null(resultado.AbsoluteNumber);
        Assert.Null(resultado.Group);
    }

    [Fact]
    public void Lee_las_etiquetas_de_identificador_de_la_carpeta()
    {
        var imdb = EpisodeNameParser.ParseSeries("Mi Serie (2019) [imdbid-tt1234567]/Temporada 01/Mi Serie S01E02.mkv");
        var anidb = EpisodeNameParser.ParseAnime("Mi Anime (2019) [anidbid-23]/[Grupo] Mi Anime - 12 [1080p].mkv");

        Assert.Equal("tt1234567", imdb.ImdbId);
        Assert.Equal("Mi Serie", imdb.SeriesTitle);
        Assert.Equal(23, anidb.AniDbId);
        Assert.Equal("Mi Anime", anidb.SeriesTitle);
        Assert.Equal(2019, anidb.SeriesYear);
    }

    [Theory]
    [InlineData("Temporada 01", 1)]
    [InlineData("temporada 10", 10)]
    [InlineData("Season 2", 2)]
    [InlineData("Season02", 2)]
    [InlineData("S03", 3)]
    [InlineData("Especiales", 0)]
    [InlineData("Specials", 0)]
    [InlineData("Mi Serie (2019)", null)]
    [InlineData("Season", null)]
    [InlineData("Extras", null)]
    public void Reconoce_las_carpetas_de_temporada(string carpeta, int? esperado) =>
        Assert.Equal(esperado, EpisodeNameParser.ParseSeasonFolder(carpeta));
}
