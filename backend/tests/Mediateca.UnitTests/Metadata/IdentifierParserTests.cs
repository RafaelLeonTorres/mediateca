using Mediateca.Core.Metadata;

namespace Mediateca.UnitTests.Metadata;

public class IdentifierParserTests
{
    [Theory]
    [InlineData("21", "anilist", "21")]
    [InlineData(" 154587 ", "anilist", "154587")]
    [InlineData("007", "anilist", "7")]
    [InlineData("https://anilist.co/anime/21", "anilist", "21")]
    [InlineData("https://anilist.co/anime/21/One-Piece/", "anilist", "21")]
    [InlineData("anilist.co/anime/154587", "anilist", "154587")]
    [InlineData("https://www.anilist.co/anime/9253/Steins-Gate?x=1", "anilist", "9253")]
    [InlineData("tt0118799", "omdb", "tt0118799")]
    [InlineData("TT0118799", "omdb", "tt0118799")]
    [InlineData("https://www.imdb.com/title/tt0118799/", "omdb", "tt0118799")]
    [InlineData("https://www.imdb.com/es/title/tt0118799/?ref_=fn_al_tt_1", "omdb", "tt0118799")]
    [InlineData("imdb.com/title/tt1234567", "omdb", "tt1234567")]
    public void Reconoce_ids_y_direcciones(string texto, string proveedor, string id)
    {
        var resultado = IdentifierParser.Parse(texto);

        Assert.NotNull(resultado);
        Assert.Equal(proveedor, resultado.Provider);
        Assert.Equal(id, resultado.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("hola")]
    [InlineData("tt12")]
    [InlineData("https://example.com/anime/21")]
    [InlineData("https://anilist.co/manga/21")]
    [InlineData("https://notanilist.co/anime/21")]
    [InlineData("https://www.imdb.com/name/nm0000093/")]
    public void Lo_que_no_es_un_identificador_devuelve_null(string? texto)
    {
        Assert.Null(IdentifierParser.Parse(texto));
    }
}
