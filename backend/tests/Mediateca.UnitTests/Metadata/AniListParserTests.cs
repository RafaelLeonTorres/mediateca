using System.Text.Json;
using Mediateca.Core.Media;
using Mediateca.Infrastructure.Metadata;

namespace Mediateca.UnitTests.Metadata;

/// <summary>Traducción de respuestas de AniList (con el mismo formato que la API real) a los campos de Mediateca.</summary>
public class AniListParserTests
{
    // Frieren: terminó, con una etiqueta de spoiler, estudios mezclados y un episodio sin número en streamingEpisodes.
    private const string Finished = """
        {
          "id": 154587,
          "idMal": 52991,
          "siteUrl": "https://anilist.co/anime/154587",
          "title": { "romaji": "Sousou no Frieren", "english": "Frieren: Beyond Journey's End", "native": "葬送のフリーレン" },
          "synonyms": ["Frieren at the Funeral", "Sousou no Frieren (TV)"],
          "description": "La maga elfa Frieren vuelve a viajar.<br><br>Es un viaje <i>muy</i> largo.<br>(Source: Crunchyroll)",
          "startDate": { "year": 2023, "month": 9, "day": 29 },
          "endDate": { "year": 2024, "month": 3, "day": 22 },
          "status": "FINISHED",
          "season": "FALL",
          "seasonYear": 2023,
          "format": "TV",
          "episodes": 28,
          "duration": 24,
          "genres": ["Adventure", "Drama", "Fantasy"],
          "tags": [
            { "name": "Elf", "rank": 70, "isMediaSpoiler": false, "isGeneralSpoiler": false },
            { "name": "Travel", "rank": 90, "isMediaSpoiler": false, "isGeneralSpoiler": false },
            { "name": "Tragedy", "rank": 95, "isMediaSpoiler": true, "isGeneralSpoiler": false },
            { "name": "Male Protagonist", "rank": 40, "isMediaSpoiler": false, "isGeneralSpoiler": true }
          ],
          "studios": { "edges": [
            { "isMain": false, "node": { "name": "Aniplex", "isAnimationStudio": false } },
            { "isMain": true, "node": { "name": "MADHOUSE", "isAnimationStudio": true } }
          ] },
          "averageScore": 91,
          "isAdult": false,
          "coverImage": { "extraLarge": "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx154587.jpg", "large": "https://s4.anilist.co/small.jpg" },
          "bannerImage": "https://s4.anilist.co/file/anilistcdn/media/anime/banner/154587.jpg",
          "nextAiringEpisode": null,
          "externalLinks": [
            { "site": "Crunchyroll", "url": "https://www.crunchyroll.com/series/frieren" },
            { "site": "Official Site", "url": "https://frieren-anime.jp/" }
          ],
          "streamingEpisodes": [
            { "title": "Episode 1 - The Journey's End", "thumbnail": "https://img.test/1.jpg" },
            { "title": "Episode 2 - It Didn't Have to Be Magic...", "thumbnail": "https://img.test/2.jpg" },
            { "title": "Episode 3 - Killing Magic", "thumbnail": "https://img.test/3.jpg" },
            { "title": "Frieren Official Trailer", "thumbnail": "https://img.test/t.jpg" }
          ]
        }
        """;

    // Una serie para adultos que sigue en emisión, sin título en inglés.
    private const string Airing = """
        {
          "id": 200001,
          "idMal": null,
          "siteUrl": "https://anilist.co/anime/200001",
          "title": { "romaji": "Aishiteru Game wo Owarasetai", "english": null, "native": "愛してるゲームを終わらせたい" },
          "synonyms": [],
          "description": null,
          "startDate": { "year": 2026, "month": 1, "day": 6 },
          "endDate": { "year": null, "month": null, "day": null },
          "status": "RELEASING",
          "seasonYear": 2026,
          "format": "TV",
          "episodes": null,
          "duration": 24,
          "genres": ["Romance"],
          "tags": [],
          "studios": { "edges": [] },
          "averageScore": null,
          "isAdult": true,
          "coverImage": { "extraLarge": null, "large": "https://s4.anilist.co/large.jpg" },
          "bannerImage": null,
          "nextAiringEpisode": { "airingAt": 1790000000, "episode": 5 },
          "externalLinks": [],
          "streamingEpisodes": []
        }
        """;

    [Fact]
    public void Traduce_el_detalle_de_una_serie_terminada()
    {
        var datos = AniListParser.ParseMedia(Finished);

        Assert.Equal("anilist", datos.Provider);
        Assert.Equal("154587", datos.ExternalId);
        Assert.Equal(154587, datos.AniListId);
        Assert.Equal("Frieren: Beyond Journey's End", datos.Title);
        Assert.Equal("Sousou no Frieren", datos.OriginalTitle);
        Assert.Equal(2023, datos.Year);
        Assert.Equal(2024, datos.EndYear);
        Assert.Equal(ReleaseStatus.Finished, datos.Status);
        Assert.Equal("TV", datos.Format);
        Assert.Equal(24, datos.RuntimeMinutes);
        Assert.Equal(28, datos.Episodes);
        Assert.Equal(new[] { "Adventure", "Drama", "Fantasy" }, datos.Genres.ToArray());
        Assert.Null(datos.ContentRating);
        Assert.Null(datos.NextAiringAt);
    }

    [Fact]
    public void La_calificacion_de_100_pasa_a_10()
    {
        Assert.Equal(9.1, AniListParser.ParseMedia(Finished).CommunityRating);
    }

    [Fact]
    public void El_resumen_html_queda_como_texto()
    {
        Assert.Equal(
            "La maga elfa Frieren vuelve a viajar.\n\nEs un viaje muy largo.",
            AniListParser.ParseMedia(Finished).Overview);
    }

    [Fact]
    public void Las_etiquetas_de_spoiler_se_excluyen_y_el_resto_va_de_mas_a_menos_relevante()
    {
        var datos = AniListParser.ParseMedia(Finished);

        Assert.Equal(new[] { "Travel", "Elf" }, datos.Tags.ToArray());
    }

    [Fact]
    public void Los_estudios_de_animacion_van_primero()
    {
        Assert.Equal(new[] { "MADHOUSE", "Aniplex" }, AniListParser.ParseMedia(Finished).Studios.ToArray());
    }

    [Fact]
    public void Trae_las_imagenes_y_los_enlaces()
    {
        var datos = AniListParser.ParseMedia(Finished);

        Assert.Equal("https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx154587.jpg", datos.PosterUrl);
        Assert.Equal("https://s4.anilist.co/file/anilistcdn/media/anime/banner/154587.jpg", datos.BackdropUrl);
        Assert.Equal(
            new[] { "AniList", "MyAnimeList", "Sitio oficial" },
            datos.Links.Select(enlace => enlace.Site).ToArray());
        Assert.Equal("https://myanimelist.net/anime/52991", datos.Links[1].Url);
    }

    [Fact]
    public void Una_serie_en_emision_para_adultos_trae_18_mas_y_el_siguiente_episodio()
    {
        var datos = AniListParser.ParseMedia(Airing);

        Assert.Equal(ReleaseStatus.Airing, datos.Status);
        Assert.Equal("18+", datos.ContentRating);
        Assert.Null(datos.EndYear);
        Assert.Null(datos.CommunityRating);
        Assert.Null(datos.Overview);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000000), datos.NextAiringAt);
        Assert.Equal(5, datos.NextAiringEpisode);
        Assert.Equal(TimeSpan.Zero, datos.NextAiringAt!.Value.Offset);
    }

    [Fact]
    public void Sin_titulo_en_ingles_el_titulo_es_el_romaji_y_el_original_el_japones()
    {
        var datos = AniListParser.ParseMedia(Airing);

        Assert.Equal("Aishiteru Game wo Owarasetai", datos.Title);
        Assert.Equal("愛してるゲームを終わらせたい", datos.OriginalTitle);
        Assert.Equal("https://s4.anilist.co/large.jpg", datos.PosterUrl);
        Assert.Equal(new[] { "AniList" }, datos.Links.Select(enlace => enlace.Site).ToArray());
    }

    [Fact]
    public void Los_titulos_de_episodios_solo_salen_de_los_que_tienen_un_numero_claro()
    {
        var datos = AniListParser.ParseMedia(Finished);

        Assert.Equal(3, datos.EpisodeTitles.Count);
        Assert.All(datos.EpisodeTitles, titulo => Assert.Null(titulo.Season));
        Assert.Equal(1, datos.EpisodeTitles[0].Number);
        Assert.Equal("The Journey's End", datos.EpisodeTitles[0].Title);
        Assert.Equal("It Didn't Have to Be Magic...", datos.EpisodeTitles[1].Title);
        Assert.Equal(3, datos.EpisodeTitles[2].Number);
    }

    [Theory]
    [InlineData("Episode 3 - The Great Enemy", 3, "The Great Enemy")]
    [InlineData("Episode 12: Un título", 12, "Un título")]
    [InlineData("Episodio 7 – Con raya larga", 7, "Con raya larga")]
    [InlineData("Ep. 5 - Corto", 5, "Corto")]
    [InlineData("E9 - Muy corto", 9, "Muy corto")]
    [InlineData("  Episode 100 - Con espacios  ", 100, "Con espacios")]
    public void Lee_el_numero_y_el_titulo_de_un_episodio(string texto, int numero, string titulo)
    {
        var resultado = AniListParser.ParseEpisodeTitle(texto);

        Assert.NotNull(resultado);
        Assert.Equal(numero, resultado.Value.Number);
        Assert.Equal(titulo, resultado.Value.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Episode 4")]
    [InlineData("Official Trailer")]
    [InlineData("Frieren PV 2 - Teaser")]
    [InlineData("Episode - Sin número")]
    public void Sin_numero_o_sin_titulo_no_se_usa(string? texto)
    {
        Assert.Null(AniListParser.ParseEpisodeTitle(texto));
    }

    [Fact]
    public void Si_un_numero_se_repite_gana_el_primero()
    {
        using var documento = JsonDocument.Parse("""
            [ { "title": "Episode 1 - Primero" }, { "title": "Episode 1 - Repetido" }, { "title": "Episode 2 - Segundo" } ]
            """);

        var titulos = AniListParser.ParseEpisodeTitles(documento.RootElement);

        Assert.Equal(2, titulos.Count);
        Assert.Equal("Primero", titulos[0].Title);
    }

    [Fact]
    public void La_busqueda_trae_todos_los_nombres_y_la_respuesta_completa()
    {
        var json = "[" + Finished + "," + Airing + "]";

        var resultados = AniListParser.ParseSearch(json);

        Assert.Equal(2, resultados.Count);
        var primero = resultados[0];
        Assert.Equal("154587", primero.ExternalId);
        Assert.Equal("Frieren: Beyond Journey's End", primero.Title);
        Assert.Equal(2023, primero.Year);
        Assert.Equal("TV", primero.Format);
        Assert.Equal("https://anilist.co/anime/154587", primero.Url);
        Assert.Contains("Sousou no Frieren", primero.Titles);
        Assert.Contains("Frieren at the Funeral", primero.Titles);
        Assert.Contains("葬送のフリーレン", primero.Titles);
        Assert.NotNull(primero.RawPayload);

        // El detalle sale de la propia búsqueda, sin otra solicitud.
        Assert.Equal("154587", AniListParser.ParseMedia(primero.RawPayload).ExternalId);
    }

    [Fact]
    public void Extrae_el_medio_de_la_respuesta_y_detecta_que_no_existe()
    {
        Assert.NotNull(AniListParser.ExtractMedia("""{ "data": { "Media": { "id": 1 } } }"""));
        Assert.Null(AniListParser.ExtractMedia("""{ "data": { "Media": null } }"""));
        Assert.Null(AniListParser.ExtractMedia("""{ "data": null }"""));
    }

    [Fact]
    public void Extrae_el_error_con_su_codigo()
    {
        var error = AniListParser.ExtractError(
            """{ "errors": [ { "message": "Not Found.", "status": 404 } ], "data": { "Media": null } }""");

        Assert.NotNull(error);
        Assert.Equal(404, error.Value.Status);
        Assert.Equal("Not Found.", error.Value.Message);
        Assert.Null(AniListParser.ExtractError("""{ "data": { "Media": { "id": 1 } } }"""));
    }
}
