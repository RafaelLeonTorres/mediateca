using Mediateca.Core.Media;
using Mediateca.Infrastructure.Metadata;

namespace Mediateca.UnitTests.Metadata;

/// <summary>Traducción de respuestas de OMDb (JSON plano con "N/A") a los campos de Mediateca.</summary>
public class OmdbParserTests
{
    private const string Movie = """
        {
          "Title": "The Matrix",
          "Year": "1999",
          "Rated": "R",
          "Released": "31 Mar 1999",
          "Runtime": "136 min",
          "Genre": "Action, Sci-Fi",
          "Director": "Lana Wachowski, Lilly Wachowski",
          "Actors": "Keanu Reeves, Laurence Fishburne, Carrie-Anne Moss",
          "Plot": "A computer hacker learns about the true nature of reality.",
          "Production": "N/A",
          "Poster": "https://m.media-amazon.com/images/M/MV5BNzQzOTk3OTAtNDQ0Zi00ZTVkLWI0MTEtMDllZjNkYzNjNTc4.jpg",
          "imdbRating": "8.7",
          "imdbID": "tt0133093",
          "Type": "movie",
          "Response": "True"
        }
        """;

    private const string Series = """
        {
          "Title": "Severance",
          "Year": "2022–",
          "Rated": "TV-MA",
          "Runtime": "N/A",
          "Genre": "Drama, Mystery, Sci-Fi",
          "Director": "N/A",
          "Actors": "Adam Scott, Britt Lower",
          "Plot": "Mark leads a team of office workers.",
          "Poster": "N/A",
          "imdbRating": "N/A",
          "imdbID": "tt11280740",
          "Type": "series",
          "totalSeasons": "2",
          "Response": "True"
        }
        """;

    [Fact]
    public void Traduce_una_pelicula()
    {
        var datos = OmdbParser.ParseDetails(Movie);

        Assert.Equal("omdb", datos.Provider);
        Assert.Equal("tt0133093", datos.ExternalId);
        Assert.Equal("tt0133093", datos.ImdbId);
        Assert.Equal("The Matrix", datos.Title);
        Assert.Equal(1999, datos.Year);
        Assert.Null(datos.EndYear);
        Assert.Null(datos.Status);
        Assert.Equal("R", datos.ContentRating);
        Assert.Equal(8.7, datos.CommunityRating);
        Assert.Equal(136, datos.RuntimeMinutes);
        Assert.Equal(new[] { "Action", "Sci-Fi" }, datos.Genres.ToArray());
        Assert.Equal(new[] { "Lana Wachowski", "Lilly Wachowski" }, datos.Directors.ToArray());
        Assert.Equal(3, datos.Actors.Count);
        Assert.Equal("A computer hacker learns about the true nature of reality.", datos.Overview);
        Assert.StartsWith("https://m.media-amazon.com/", datos.PosterUrl);
        Assert.Null(datos.BackdropUrl);
        Assert.Equal("movie", datos.Format);
        Assert.Equal("IMDb", Assert.Single(datos.Links).Site);
        Assert.Equal("https://www.imdb.com/title/tt0133093/", datos.Links[0].Url);
    }

    [Fact]
    public void Los_N_A_son_datos_que_faltan()
    {
        var datos = OmdbParser.ParseDetails(Series);

        Assert.Null(datos.RuntimeMinutes);
        Assert.Null(datos.CommunityRating);
        Assert.Null(datos.PosterUrl);
        Assert.Empty(datos.Directors);
        Assert.Empty(datos.Studios);
    }

    [Fact]
    public void Una_serie_con_raya_final_sigue_en_emision()
    {
        var datos = OmdbParser.ParseDetails(Series);

        Assert.Equal(2022, datos.Year);
        Assert.Null(datos.EndYear);
        Assert.Equal(ReleaseStatus.Airing, datos.Status);
        Assert.Equal(2, datos.TotalSeasons);
        Assert.Equal("TV-MA", datos.ContentRating);
    }

    [Fact]
    public void Una_serie_con_ano_final_esta_terminada()
    {
        var datos = OmdbParser.ParseDetails(Series.Replace("2022–", "2011–2019"));

        Assert.Equal(2011, datos.Year);
        Assert.Equal(2019, datos.EndYear);
        Assert.Equal(ReleaseStatus.Finished, datos.Status);
    }

    [Theory]
    [InlineData("Not Rated")]
    [InlineData("Unrated")]
    [InlineData("N/A")]
    public void Sin_clasificacion_no_hay_clasificacion(string clasificacion)
    {
        var datos = OmdbParser.ParseDetails(Movie.Replace("\"Rated\": \"R\"", $"\"Rated\": \"{clasificacion}\""));

        Assert.Null(datos.ContentRating);
    }

    [Theory]
    [InlineData("1999", 1999, null, false)]
    [InlineData("2019–", 2019, null, true)]
    [InlineData("2019–2021", 2019, 2021, false)]
    [InlineData("2019-2021", 2019, 2021, false)]
    [InlineData("2019 – ", 2019, null, true)]
    [InlineData("N/A", null, null, false)]
    [InlineData("hace mucho", null, null, false)]
    [InlineData(null, null, null, false)]
    public void Lee_los_anos(string? texto, int? inicio, int? fin, bool sigue)
    {
        var (year, endYear, ongoing) = OmdbParser.ParseYears(texto);

        Assert.Equal(inicio, year);
        Assert.Equal(fin, endYear);
        Assert.Equal(sigue, ongoing);
    }

    [Fact]
    public void Traduce_la_busqueda()
    {
        const string json = """
            { "Search": [
                { "Title": "The Matrix", "Year": "1999", "imdbID": "tt0133093", "Type": "movie", "Poster": "https://m.media-amazon.com/images/M/a.jpg" },
                { "Title": "The Matrix Reloaded", "Year": "2003", "imdbID": "tt0234215", "Type": "movie", "Poster": "N/A" },
                { "Title": "Matrix", "Year": "1993–1993", "imdbID": "tt0106062", "Type": "series", "Poster": "N/A" }
              ], "totalResults": "3", "Response": "True" }
            """;

        var resultados = OmdbParser.ParseSearch(json);

        Assert.Equal(3, resultados.Count);
        Assert.Equal("tt0133093", resultados[0].ExternalId);
        Assert.Equal(1999, resultados[0].Year);
        Assert.Equal("movie", resultados[0].Format);
        Assert.NotNull(resultados[0].PosterUrl);
        Assert.Null(resultados[1].PosterUrl);
        Assert.Equal("series", resultados[2].Format);
        Assert.Equal("https://www.imdb.com/title/tt0106062/", resultados[2].Url);
    }

    [Fact]
    public void Una_busqueda_sin_resultados_da_una_lista_vacia()
    {
        Assert.Empty(OmdbParser.ParseSearch("""{ "Response": "False", "Error": "Movie not found!" }"""));
    }

    [Fact]
    public void Traduce_los_titulos_de_una_temporada_y_omite_los_que_no_tienen()
    {
        const string json = """
            { "Title": "Severance", "Season": "1", "totalSeasons": "2", "Episodes": [
                { "Title": "Good News About Hell", "Released": "2022-02-18", "Episode": "1", "imdbRating": "8.7", "imdbID": "tt11280770" },
                { "Title": "Half Loop", "Released": "2022-02-18", "Episode": "2", "imdbRating": "8.5", "imdbID": "tt11280771" },
                { "Title": "N/A", "Released": "N/A", "Episode": "3", "imdbRating": "N/A", "imdbID": "tt0" },
                { "Title": "Sin número", "Episode": "N/A" }
              ], "Response": "True" }
            """;

        var titulos = OmdbParser.ParseSeason(json, 1);

        Assert.Equal(2, titulos.Count);
        Assert.Equal(1, titulos[0].Season);
        Assert.Equal(1, titulos[0].Number);
        Assert.Equal("Good News About Hell", titulos[0].Title);
        Assert.Equal(2, titulos[1].Number);
    }

    [Theory]
    [InlineData("""{ "Response": "False", "Error": "Movie not found!" }""", "Movie not found!")]
    [InlineData("""{ "Response": "False", "Error": "Request limit reached!" }""", "Request limit reached!")]
    [InlineData("""{ "Response": "False" }""", "Error desconocido")]
    [InlineData("""{ "Response": "True", "Title": "X" }""", null)]
    public void Detecta_los_errores_de_la_respuesta(string json, string? esperado)
    {
        Assert.Equal(esperado, OmdbParser.GetError(json));
    }

    [Fact]
    public void Distingue_el_limite_diario_y_la_clave_invalida()
    {
        Assert.True(OmdbParser.IsDailyLimit("Request limit reached!"));
        Assert.False(OmdbParser.IsDailyLimit("Movie not found!"));
        Assert.True(OmdbParser.IsInvalidKey("Invalid API key!"));
        Assert.True(OmdbParser.IsInvalidKey("No API key provided."));
        Assert.False(OmdbParser.IsInvalidKey("Movie not found!"));
    }

    [Fact]
    public void Una_respuesta_sin_el_id_de_imdb_es_un_error_del_proveedor()
    {
        Assert.Throws<Mediateca.Core.Metadata.MetadataProviderException>(
            () => OmdbParser.ParseDetails("""{ "Title": "X", "Response": "True" }"""));
    }
}
