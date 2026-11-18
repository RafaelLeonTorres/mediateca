using Mediateca.Core.Subtitles;
using Xunit;

namespace Mediateca.UnitTests.Subtitles;

public class SubtitleLabelsTests
{
    private static SubtitleTrack Track(
        string id,
        string? language,
        bool forced = false,
        string? title = null,
        SubtitleOrigin origin = SubtitleOrigin.Embedded) =>
        new(id, string.Empty, language, "subrip", origin, false, forced, title, origin == SubtitleOrigin.Embedded ? 1 : null, null);

    private static string[] Labels(params SubtitleTrack[] tracks) =>
        [.. SubtitleLabels.Assign(tracks).Select(t => t.Label)];

    [Fact]
    public void Usa_el_nombre_del_idioma_en_espanol()
    {
        Assert.Equal(
            new[] { "Español", "Inglés", "Japonés" },
            Labels(Track("e1", "es"), Track("e2", "en"), Track("e3", "ja")));
    }

    [Fact]
    public void Agrega_forzados_cuando_aplica()
    {
        Assert.Equal(new[] { "Español", "Español (forzados)" }, Labels(Track("e1", "es"), Track("e2", "es", forced: true)));
    }

    [Fact]
    public void Sin_idioma_se_llama_Pista_con_su_posicion()
    {
        Assert.Equal(
            new[] { "Inglés", "Español", "Pista 3 (forzados)", "Pista 4" },
            Labels(Track("e1", "en"), Track("e2", "es"), Track("e3", null, forced: true), Track("e4", null)));
    }

    [Fact]
    public void Un_idioma_desconocido_se_muestra_en_mayusculas()
    {
        Assert.Equal(new[] { "RUS" }, Labels(Track("e1", "rus")));
    }

    [Fact]
    public void Distingue_las_repetidas_por_su_titulo_cuando_todas_lo_traen()
    {
        Assert.Equal(
            new[] { "Inglés – SDH", "Inglés – Comentarios" },
            Labels(Track("e1", "en", title: "SDH"), Track("e2", "en", title: "Comentarios")));
    }

    [Fact]
    public void Distingue_las_repetidas_por_su_origen()
    {
        Assert.Equal(
            new[] { "Español (incrustados)", "Español (archivo)" },
            Labels(Track("e1", "es"), Track("x0", "es", origin: SubtitleOrigin.External)));
    }

    [Fact]
    public void Si_siguen_iguales_las_numera_dejando_la_primera_como_esta()
    {
        Assert.Equal(
            new[] { "Inglés", "Inglés (2)", "Inglés (3)" },
            Labels(Track("e1", "en"), Track("e2", "en"), Track("e3", "en")));
    }

    [Fact]
    public void Con_titulos_a_medias_pasa_al_origen_y_despues_al_numero()
    {
        var labels = Labels(
            Track("e1", "es", title: "Latino"),
            Track("e2", "es"),
            Track("x0", "es", origin: SubtitleOrigin.External));

        Assert.Equal(new[] { "Español (incrustados)", "Español (incrustados) (2)", "Español (archivo)" }, labels);
    }

    [Fact]
    public void No_toca_el_resto_de_los_datos_de_la_pista()
    {
        var original = Track("e5", "es", forced: true, title: "Forzados");

        var result = Assert.Single(SubtitleLabels.Assign([original]));

        Assert.Equal(original with { Label = "Español (forzados)" }, result);
    }

    [Fact]
    public void Las_subidas_lo_dicen_y_sin_idioma_se_llaman_como_su_archivo()
    {
        Assert.Equal(
            new[] { "Español", "Español (subido)", "Inglés (forzados, subido)", "Mi archivo (subido)", "Pista 5 (subido)" },
            Labels(
                Track("e1", "es"),
                Track("u1", "es", origin: SubtitleOrigin.Uploaded),
                Track("u2", "en", forced: true, origin: SubtitleOrigin.Uploaded),
                Track("u3", null, title: "Mi archivo", origin: SubtitleOrigin.Uploaded),
                Track("u4", null, origin: SubtitleOrigin.Uploaded)));
    }

    [Fact]
    public void Dos_subidas_del_mismo_idioma_se_distinguen_por_su_archivo()
    {
        Assert.Equal(
            new[] { "Español (subido) – Episodio 01", "Español (subido) – Episodio 01 v2" },
            Labels(
                Track("u1", "es", title: "Episodio 01", origin: SubtitleOrigin.Uploaded),
                Track("u2", "es", title: "Episodio 01 v2", origin: SubtitleOrigin.Uploaded)));
    }

    [Fact]
    public void Una_lista_vacia_da_una_lista_vacia() => Assert.Empty(SubtitleLabels.Assign([]));
}
