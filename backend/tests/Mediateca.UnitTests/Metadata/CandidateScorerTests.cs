using Mediateca.Core.Media;
using Mediateca.Core.Metadata;

namespace Mediateca.UnitTests.Metadata;

/// <summary>
/// Qué resultado de AniList se elige para las carpetas reales de 2026. Los resultados de cada caso son los que
/// devuelve la búsqueda de AniList (títulos, año y formato), en el orden del proveedor.
/// </summary>
public class CandidateScorerTests
{
    private static SearchCandidate Entry(
        string[] titles,
        int year,
        string format = "TV",
        ReleaseStatus status = ReleaseStatus.Finished,
        int? endYear = null) =>
        new(
            ExternalId: Math.Abs(string.Concat(titles).GetHashCode(StringComparison.Ordinal)).ToString(),
            Title: titles[^1],
            OriginalTitle: titles[0],
            Titles: titles,
            Year: year,
            EndYear: endYear,
            Status: status,
            Format: format,
            PosterUrl: null,
            Url: null);

    private static string? Pick(string carpeta, params SearchCandidate[] resultados) =>
        CandidateScorer.PickBest(SearchNameCleaner.Clean(carpeta), resultados)?.Candidate.Titles[0];

    [Fact]
    public void Con_la_temporada_en_la_carpeta_elige_la_temporada()
    {
        var elegido = Pick(
            "Sousou no Frieren season 2",
            Entry(["Sousou no Frieren", "Frieren: Beyond Journey's End"], 2023, endYear: 2024),
            Entry(["Sousou no Frieren 2nd Season", "Frieren: Beyond Journey's End Season 2"], 2026, status: ReleaseStatus.Airing),
            Entry(["Sousou no Frieren: ★7-nin no Mahoutsukai-hen"], 2024, "SPECIAL"));

        Assert.Equal("Sousou no Frieren 2nd Season", elegido);
    }

    [Fact]
    public void Con_el_ano_de_la_carpeta_elige_la_que_se_emite_ese_ano()
    {
        var elegido = Pick(
            "Boku no Hero Academia (2026)",
            Entry(["Boku no Hero Academia", "My Hero Academia"], 2016, endYear: 2016),
            Entry(["Boku no Hero Academia 7th Season", "My Hero Academia Season 7"], 2024, endYear: 2024),
            Entry(["Boku no Hero Academia: Final Season", "My Hero Academia: Final Season"], 2025, status: ReleaseStatus.Airing),
            Entry(["Boku no Hero Academia: Heroes Rising"], 2019, "MOVIE"));

        Assert.Equal("Boku no Hero Academia: Final Season", elegido);
    }

    [Fact]
    public void Un_ano_lejano_pesa_mas_que_un_titulo_identico()
    {
        // "Hokuto no Ken (2026)": el original de 1984 se llama igual, pero el de 2026 es el que se emite.
        var elegido = Pick(
            "Hokuto no Ken (2026)",
            Entry(["Hokuto no Ken", "Fist of the North Star"], 1984, endYear: 1987),
            Entry(["Hokuto no Ken 2"], 1987, endYear: 1988),
            Entry(["Hokuto no Ken: Kenou-gun no Zako-tachi no Banka"], 2026, status: ReleaseStatus.Airing));

        Assert.Equal("Hokuto no Ken: Kenou-gun no Zako-tachi no Banka", elegido);
    }

    [Fact]
    public void Un_numero_romano_es_la_temporada()
    {
        var elegido = Pick(
            "Youjo Senki II",
            Entry(["Youjo Senki", "Saga of Tanya the Evil"], 2017),
            Entry(["Youjo Senki Movie", "Saga of Tanya the Evil: The Movie"], 2019, "MOVIE"),
            Entry(["Youjo Senki II", "Saga of Tanya the Evil II"], 2026, status: ReleaseStatus.Airing));

        Assert.Equal("Youjo Senki II", elegido);
    }

    [Fact]
    public void El_simbolo_romano_de_un_solo_caracter_es_la_temporada_3()
    {
        var elegido = Pick(
            "Mushoku Tensei Ⅲ Isekai Ittara Honki Dasu",
            Entry(["Mushoku Tensei: Isekai Ittara Honki Dasu", "Mushoku Tensei: Jobless Reincarnation"], 2021),
            Entry(["Mushoku Tensei: Isekai Ittara Honki Dasu 2nd Season"], 2023),
            Entry(["Mushoku Tensei: Isekai Ittara Honki Dasu 3rd Season", "Mushoku Tensei: Jobless Reincarnation Season 3"], 2026, status: ReleaseStatus.Airing));

        Assert.Equal("Mushoku Tensei: Isekai Ittara Honki Dasu 3rd Season", elegido);
    }

    [Fact]
    public void Una_carpeta_con_el_ano_y_la_palabra_movie_elige_la_pelicula()
    {
        var elegido = Pick(
            "Gintama the Movie 2026 Yoshiwara in Flames",
            Entry(["Gintama", "Gintama"], 2006),
            Entry(["Gintama: Yoshiwara Enjou-hen", "Gintama: Yoshiwara in Flames"], 2026, "MOVIE"),
            Entry(["Gintama: The Movie", "Gintama: The Movie"], 2010, "MOVIE"));

        Assert.Equal("Gintama: Yoshiwara Enjou-hen", elegido);
    }

    [Fact]
    public void Una_carpeta_con_Movie_prefiere_la_pelicula_a_la_serie()
    {
        var elegido = Pick(
            "Boku no Kokoro no Yabai Yatsu Movie",
            Entry(["Boku no Kokoro no Yabai Yatsu", "The Dangers in My Heart"], 2023),
            Entry(["Boku no Kokoro no Yabai Yatsu 2nd Season"], 2025),
            Entry(["Boku no Kokoro no Yabai Yatsu Movie", "The Dangers in My Heart Movie"], 2026, "MOVIE"));

        Assert.Equal("Boku no Kokoro no Yabai Yatsu Movie", elegido);
    }

    [Fact]
    public void Una_pelicula_con_numero_se_encuentra_aunque_el_titulo_no_sea_igual()
    {
        var elegido = Pick(
            "Girls Band Cry Movie 01 Seishun Kyousoukyoku",
            Entry(["Girls Band Cry", "Girls Band Cry"], 2024),
            Entry(["Girls Band Cry Movie: Zenpen Seishun Kyousoukyoku", "Girls Band Cry Movie 01"], 2025, "MOVIE"));

        Assert.Equal("Girls Band Cry Movie: Zenpen Seishun Kyousoukyoku", elegido);
    }

    [Fact]
    public void La_pelicula_de_Slime_con_numero_no_se_confunde_con_la_primera()
    {
        var elegido = Pick(
            "tensei shitara slime datta ken movie 2 soukai no namida-hen",
            Entry(["Tensei shitara Slime Datta Ken", "That Time I Got Reincarnated as a Slime"], 2018),
            Entry(["Tensei shitara Slime Datta Ken Movie: Guren no Kizuna-hen"], 2022, "MOVIE"),
            Entry(["Tensei shitara Slime Datta Ken Movie 2: Soukai no Namida-hen"], 2026, "MOVIE"));

        Assert.Equal("Tensei shitara Slime Datta Ken Movie 2: Soukai no Namida-hen", elegido);
    }

    [Fact]
    public void El_titulo_en_ingles_entre_parentesis_tambien_cuenta()
    {
        var elegido = Pick(
            "Koori no Jouheki 2nd Season (The Ramparts of Ice Season 2)",
            Entry(["Koori no Jouheki", "The Ramparts of Ice"], 2025),
            Entry(["Koori no Jouheki 2nd Season", "The Ramparts of Ice Season 2"], 2026, status: ReleaseStatus.Airing));

        Assert.Equal("Koori no Jouheki 2nd Season", elegido);
    }

    [Fact]
    public void Una_carpeta_con_el_parentesis_sin_cerrar_elige_la_temporada_4()
    {
        var elegido = Pick(
            "Mairimashita! Iruma-kun 4th Season (Welcome to Demon School! Iruma-kun Season 4",
            Entry(["Mairimashita! Iruma-kun 3rd Season", "Welcome to Demon School! Iruma-kun Season 3"], 2022),
            Entry(["Mairimashita! Iruma-kun 4th Season", "Welcome to Demon School! Iruma-kun Season 4"], 2026),
            Entry(["Mairimashita! Iruma-kun"], 2019));

        Assert.Equal("Mairimashita! Iruma-kun 4th Season", elegido);
    }

    [Fact]
    public void ReZero_sin_dos_puntos_se_parece_al_titulo_con_ellos()
    {
        var elegido = Pick(
            "ReZero kara Hajimeru Isekai Seikatsu 4th Season",
            Entry(["Re:Zero kara Hajimeru Isekai Seikatsu", "Re:ZERO -Starting Life in Another World-"], 2016),
            Entry(["Re:Zero kara Hajimeru Isekai Seikatsu 4th Season"], 2026, status: ReleaseStatus.Airing),
            Entry(["Re:Zero kara Hajimeru Isekai Seikatsu 3rd Season"], 2024));

        Assert.Equal("Re:Zero kara Hajimeru Isekai Seikatsu 4th Season", elegido);
    }

    [Fact]
    public void Las_letras_matematicas_en_negrita_se_encuentran()
    {
        var elegido = Pick(
            "𝐔𝐫𝐮𝐰𝐚𝐬𝐡𝐢 𝐧𝐨 𝐘𝐨𝐢 𝐧𝐨 𝐓𝐬𝐮𝐤𝐢",
            Entry(["Uruwashi no Yoi no Tsuki"], 2026),
            Entry(["Tsuki ga Michibiku Isekai Douchuu"], 2021));

        Assert.Equal("Uruwashi no Yoi no Tsuki", elegido);
    }

    [Fact]
    public void Una_o_cirilica_dentro_del_nombre_no_impide_encontrarlo()
    {
        var elegido = Pick(
            "Juuоu Mujin Dandivine",
            Entry(["Juuou Mujin no Fafnir"], 2015),
            Entry(["Juuou Mujin Dandivine", "Dandivine"], 2026));

        Assert.Equal("Juuou Mujin Dandivine", elegido);
    }

    [Fact]
    public void La_temporada_con_el_titulo_japones_se_encuentra_por_el_numero()
    {
        var elegido = Pick(
            "Fire Force Seasson 3",
            Entry(["Enen no Shouboutai", "Fire Force"], 2019),
            Entry(["Enen no Shouboutai: Ni no Shou", "Fire Force Season 2"], 2020),
            Entry(["Enen no Shouboutai: San no Shou", "Fire Force Season 3"], 2025));

        Assert.Equal("Enen no Shouboutai: San no Shou", elegido);
    }

    [Fact]
    public void Sin_ano_ni_temporada_prefiere_el_titulo_mas_parecido()
    {
        var elegido = Pick(
            "Tomb Raider King",
            Entry(["Tomb Raider King"], 2026),
            Entry(["Tomb Raider"], 2001, "MOVIE"));

        Assert.Equal("Tomb Raider King", elegido);
    }

    [Fact]
    public void Un_titulo_corto_exacto_gana_a_uno_parecido()
    {
        var elegido = Pick("MAO", Entry(["MAO"], 2019), Entry(["Mao-chan"], 2017));

        Assert.Equal("MAO", elegido);
    }

    [Fact]
    public void Un_sufijo_raro_pegado_al_nombre_todavia_se_acepta()
    {
        var elegido = Pick(
            "Shibou Yuugi de Meshi wo Kuu. 44CLOUDY BEACH",
            Entry(["Shibou Yuugi de Meshi wo Kuu."], 2026),
            Entry(["Shokugeki no Soma"], 2015));

        Assert.Equal("Shibou Yuugi de Meshi wo Kuu.", elegido);
    }

    [Fact]
    public void La_S2_abreviada_cuenta_como_temporada()
    {
        var elegido = Pick(
            "Trigun Stargaze S2",
            Entry(["Trigun Stargaze", "Trigun Stargaze"], 2023),
            Entry(["Trigun", "Trigun"], 1998));

        Assert.Equal("Trigun Stargaze", elegido);
    }

    [Fact]
    public void Si_ningun_resultado_se_parece_no_se_elige_nada()
    {
        var elegido = Pick(
            "Zzzz Qqqq Wwww",
            Entry(["Sousou no Frieren"], 2023),
            Entry(["Kusuriya no Hitorigoto"], 2023));

        Assert.Null(elegido);
    }

    [Fact]
    public void Una_busqueda_sin_resultados_no_elige_nada()
    {
        Assert.Null(CandidateScorer.PickBest(SearchNameCleaner.Clean("Lo que sea"), []));
    }

    [Fact]
    public void El_orden_de_Rank_va_del_mejor_al_peor_y_conserva_todos()
    {
        var ranking = CandidateScorer.Rank(
            SearchNameCleaner.Clean("Kusuriya no Hitorigoto Season 3"),
            [
                Entry(["Kusuriya no Hitorigoto", "The Apothecary Diaries"], 2023),
                Entry(["Kusuriya no Hitorigoto 2nd Season"], 2025),
                Entry(["Kusuriya no Hitorigoto 3rd Season"], 2026),
            ]);

        Assert.Equal(3, ranking.Count);
        Assert.Equal("Kusuriya no Hitorigoto 3rd Season", ranking[0].Candidate.Titles[0]);
        Assert.True(ranking[0].Score >= ranking[1].Score && ranking[1].Score >= ranking[2].Score);
    }

    [Fact]
    public void En_OMDb_el_formato_no_puntua()
    {
        // OMDb ya filtra películas o series en la búsqueda: UseFormat es falso y "movie" no resta ni suma.
        var hint = SearchNameCleaner.Clean("The Matrix");
        var ranking = CandidateScorer.Rank(
            hint,
            [Entry(["The Matrix"], 1999, "movie")],
            new ScoringOptions(UseFormat: false));

        Assert.Equal(1.0, ranking[0].Score, precision: 3);
    }
}
