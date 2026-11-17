using Mediateca.Core.Naming;
using Xunit;

namespace Mediateca.UnitTests.Naming;

public class SortTitlesTests
{
    [Theory]
    [InlineData("The Matrix", "matrix")]
    [InlineData("El Señor de los Anillos", "senor de los anillos")]
    [InlineData("Un Cuento", "cuento")]
    [InlineData("Una Cosa", "cosa")]
    [InlineData("A Beautiful Mind", "beautiful mind")]
    [InlineData("An Education", "education")]
    [InlineData("Las Vidas", "vidas")]
    [InlineData("Los Otros", "otros")]
    [InlineData("  La   Casa ", "casa")]
    [InlineData("Ángel", "angel")]
    [InlineData("Ñandú", "nandu")]
    [InlineData("Amélie", "amelie")]
    // Solo se quita el artículo cuando es una palabra completa y queda algo después.
    [InlineData("Anime", "anime")]
    [InlineData("The", "the")]
    [InlineData("Elefante", "elefante")]
    public void Ordena_sin_articulo_acentos_ni_mayusculas(string titulo, string esperado) =>
        Assert.Equal(esperado, SortTitles.From(titulo));

    [Fact]
    public void Recorta_los_titulos_muy_largos()
    {
        var resultado = SortTitles.From(new string('x', 800));

        Assert.Equal(500, resultado.Length);
    }
}
