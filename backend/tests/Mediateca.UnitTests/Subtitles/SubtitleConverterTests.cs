using System.Text;
using Mediateca.Core.Subtitles;
using Xunit;

namespace Mediateca.UnitTests.Subtitles;

public class SubtitleConverterTests
{
    [Fact]
    public void Convierte_un_SRT_con_BOM_saltos_de_Windows_indices_y_coma_en_los_tiempos()
    {
        var srt = "﻿1\r\n00:00:01,500 --> 00:00:03,000\r\nHola\r\n\r\n2\r\n00:01:02,5 --> 01:02:03,456\r\n<i>Adiós</i>\r\nsegunda línea\r\n\r\n";

        var vtt = SubtitleConverter.SrtToVtt(srt);

        Assert.Equal(
            "WEBVTT\n\n00:00:01.500 --> 00:00:03.000\nHola\n\n00:01:02.500 --> 01:02:03.456\n<i>Adiós</i>\nsegunda línea\n\n",
            vtt);
    }

    [Fact]
    public void Un_SRT_vacio_da_un_WebVTT_sin_cues()
    {
        Assert.Equal("WEBVTT\n\n", SubtitleConverter.SrtToVtt(string.Empty));
        Assert.Equal("WEBVTT\n\n", SubtitleConverter.SrtToVtt(null));
        Assert.Equal("WEBVTT\n\n", SubtitleConverter.SrtToVtt("texto que no es un subtítulo\n\nni este"));
    }

    [Fact]
    public void Quita_las_etiquetas_de_estilo_que_WebVTT_no_conoce()
    {
        var srt = "1\n00:00:01,000 --> 00:00:02,000\n{\\an8}<font color=\"#ff0000\">Texto</font>\n<b>negrita</b>\n";

        var vtt = SubtitleConverter.SrtToVtt(srt);

        Assert.Equal("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nTexto\n<b>negrita</b>\n\n", vtt);
    }

    [Fact]
    public void Escapa_el_menor_que_suelto_pero_deja_las_etiquetas_de_WebVTT()
    {
        var srt = "1\n00:00:01,000 --> 00:00:02,000\nMe quieres <3 <i>mucho</i>\n";

        Assert.Contains("Me quieres &lt;3 <i>mucho</i>", SubtitleConverter.SrtToVtt(srt));
    }

    [Fact]
    public void Separa_los_cues_aunque_falte_la_linea_en_blanco_entre_ellos()
    {
        var srt = "1\n00:00:01,000 --> 00:00:02,000\nUno\n2\n00:00:03,000 --> 00:00:04,000\nDos\n";

        var vtt = SubtitleConverter.SrtToVtt(srt);

        Assert.Equal(
            "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nUno\n\n00:00:03.000 --> 00:00:04.000\nDos\n\n",
            vtt);
    }

    [Fact]
    public void Omite_los_cues_sin_texto()
    {
        var srt = "1\n00:00:01,000 --> 00:00:02,000\n{\\an8}\n\n2\n00:00:03,000 --> 00:00:04,000\nSí\n";

        Assert.Equal("WEBVTT\n\n00:00:03.000 --> 00:00:04.000\nSí\n\n", SubtitleConverter.SrtToVtt(srt));
    }

    private const string Ass =
        "[Script Info]\nTitle: Prueba\n\n[V4+ Styles]\nFormat: Name, Fontname\nStyle: Default,Arial\n\n[Events]\n" +
        "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
        "Dialogue: 0,0:00:05.00,0:00:07.50,Default,,0,0,0,,{\\an8}Segunda, con coma\\NSalto\n" +
        "Dialogue: 0,0:00:01.00,0:00:03.25,Default,,0,0,0,,{\\i1}Primera{\\i0}\n" +
        "Comment: 0,0:00:09.00,0:00:10.00,Default,,0,0,0,,No sale\n" +
        "Dialogue: 0,0:00:11.00,0:00:12.00,Default,,0,0,0,,{\\p1}m 0 0 l 10 0 10 10{\\p0}\n";

    [Fact]
    public void Convierte_un_ASS_con_las_lineas_Dialogue_sin_etiquetas_y_ordenadas()
    {
        var vtt = SubtitleConverter.AssToVtt(Ass);

        Assert.Equal(
            "WEBVTT\n\n00:00:01.000 --> 00:00:03.250\nPrimera\n\n00:00:05.000 --> 00:00:07.500\nSegunda, con coma\nSalto\n\n",
            vtt);
    }

    [Fact]
    public void Convierte_un_ASS_con_saltos_de_Windows()
    {
        Assert.Equal(SubtitleConverter.AssToVtt(Ass), SubtitleConverter.AssToVtt(Ass.Replace("\n", "\r\n")));
    }

    [Fact]
    public void Un_SSA_usa_las_columnas_que_dice_su_Format()
    {
        var ssa =
            "[Events]\nFormat: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: Marked=0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hola\\nmundo\n";

        // \n es un salto "suave": en pantalla es un espacio. Solo \N rompe la línea.
        Assert.Equal("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHola mundo\n\n", SubtitleConverter.AssToVtt(ssa));
    }

    [Fact]
    public void Un_ASS_sin_Format_usa_las_columnas_estandar_y_escapa_los_simbolos()
    {
        var ass = "[Events]\nDialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Tom & Jerry <3\n";

        Assert.Equal("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nTom &amp; Jerry &lt;3\n\n", SubtitleConverter.AssToVtt(ass));
    }

    [Fact]
    public void Un_ASS_vacio_o_sin_eventos_da_un_WebVTT_sin_cues()
    {
        Assert.Equal("WEBVTT\n\n", SubtitleConverter.AssToVtt(null));
        Assert.Equal("WEBVTT\n\n", SubtitleConverter.AssToVtt("[Script Info]\nTitle: nada\n"));
    }

    [Fact]
    public void Lee_UTF8_con_y_sin_BOM_y_UTF16()
    {
        var text = "Canción mañana";

        Assert.Equal(text, SubtitleConverter.Decode(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(text, SubtitleConverter.Decode([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]));
        Assert.Equal(text, SubtitleConverter.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)]));
        Assert.Equal(text, SubtitleConverter.Decode([.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)]));
    }

    [Fact]
    public void Lo_que_no_es_UTF8_se_lee_como_Latin1_y_conserva_acentos_y_eñes()
    {
        var text = "Canción de mañana, ¿vamos?";

        Assert.Equal(text, SubtitleConverter.Decode(Encoding.Latin1.GetBytes(text)));
    }
}
