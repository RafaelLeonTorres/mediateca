using Mediateca.Core.Metadata;

namespace Mediateca.UnitTests.Metadata;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Sousou no Frieren", "sousou no frieren")]
    [InlineData("Re:Zero kara Hajimeru Isekai Seikatsu", "re zero kara hajimeru isekai seikatsu")]
    [InlineData("JoJo’s Bizarre Adventure", "jojos bizarre adventure")]
    [InlineData("Ōkami-san to Shichinin no Nakama-tachi", "okami san to shichinin no nakama tachi")]
    [InlineData("  Mucho   espacio  ", "mucho espacio")]
    // Letras matemáticas y de ancho completo.
    [InlineData("𝐔𝐫𝐮𝐰𝐚𝐬𝐡𝐢 𝐧𝐨 𝐘𝐨𝐢 𝐧𝐨 𝐓𝐬𝐮𝐤𝐢", "uruwashi no yoi no tsuki")]
    [InlineData("ＭＡＯ　２０２６", "mao 2026")]
    // Un numeral romano de un solo símbolo.
    [InlineData("Mushoku Tensei Ⅲ", "mushoku tensei iii")]
    // Cirílico con letras que se ven latinas, dentro de un nombre latino.
    [InlineData("Juuоu Mujin Dandivine", "juuou mujin dandivine")]
    public void Normaliza_nombres(string texto, string esperado)
    {
        Assert.Equal(esperado, NameNormalizer.Normalize(texto));
    }

    [Fact]
    public void Un_titulo_ruso_no_se_convierte_a_letras_latinas()
    {
        Assert.Equal("привет мир", NameNormalizer.Normalize("Привет, мир"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!")]
    public void Sin_letras_ni_digitos_queda_vacio(string? texto)
    {
        Assert.Equal("", NameNormalizer.Normalize(texto));
    }

    [Fact]
    public void Dos_nombres_iguales_valen_uno()
    {
        Assert.Equal(1.0, NameNormalizer.Similarity("sousou no frieren", "sousou no frieren"));
    }

    [Fact]
    public void Solo_cambian_los_espacios_vale_casi_uno()
    {
        Assert.Equal(0.98, NameNormalizer.Similarity("rezero", "re zero"));
    }

    [Fact]
    public void Un_nombre_contenido_en_el_otro_vale_mas_que_uno_distinto()
    {
        var contenido = NameNormalizer.Similarity("sword art online", "sword art online alicization");
        var distinto = NameNormalizer.Similarity("sword art online", "tsuki ga michibiku isekai douchuu");

        Assert.True(contenido > 0.6, $"contenido = {contenido}");
        Assert.True(distinto < 0.4, $"distinto = {distinto}");
    }

    [Fact]
    public void Un_nombre_vacio_no_se_parece_a_nada()
    {
        Assert.Equal(0, NameNormalizer.Similarity("", "frieren"));
        Assert.Equal(0, NameNormalizer.Similarity(null, null));
    }
}
