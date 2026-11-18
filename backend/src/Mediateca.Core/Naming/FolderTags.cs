using System.Globalization;
using System.Text.RegularExpressions;

namespace Mediateca.Core.Naming;

/// <summary>Nombre sin las etiquetas de identificador, y los identificadores que traía.</summary>
public sealed record TaggedName(string Name, string? ImdbId, int? AniListId);

/// <summary>
/// Etiquetas opcionales en el nombre de una carpeta o de un archivo para no depender de la
/// búsqueda: <c>[imdbid-tt0118799]</c> y <c>[anilistid-21]</c> (PLAN.md, secciones 7 y 8).
/// La etiqueta <c>[anidbid-23]</c> de las versiones anteriores se sigue reconociendo, pero solo para
/// quitarla del nombre: un id de AniDB no sirve en AniList, así que su valor se descarta.
/// </summary>
public static class FolderTags
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex Imdb = new(
        @"[\[\{\(]\s*imdb(?:id)?\s*[-=]\s*(tt[0-9]{5,10})\s*[\]\}\)]", Options);

    private static readonly Regex AniList = new(
        @"[\[\{\(]\s*anilist(?:id)?\s*[-=]\s*([0-9]{1,9})\s*[\]\}\)]", Options);

    // Etiqueta de AniDB (fase 2): se quita del nombre y se ignora su valor.
    private static readonly Regex AniDb = new(
        @"[\[\{\(]\s*anidb(?:id)?\s*[-=]\s*[0-9]{1,9}\s*[\]\}\)]", Options);

    /// <summary>Quita las etiquetas del nombre y devuelve los identificadores que encontró.</summary>
    public static TaggedName Extract(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string? imdbId = null;
        int? aniListId = null;

        var imdb = Imdb.Match(name);
        if (imdb.Success)
        {
            imdbId = imdb.Groups[1].Value.ToLowerInvariant();
            name = Imdb.Replace(name, " ");
        }

        var aniList = AniList.Match(name);
        if (aniList.Success)
        {
            // Un número fuera de rango no es un id válido: se quita la etiqueta pero no se usa.
            if (int.TryParse(aniList.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                aniListId = id;
            }

            name = AniList.Replace(name, " ");
        }

        name = AniDb.Replace(name, " ");

        return new TaggedName(TitleParsing.Collapse(name), imdbId, aniListId);
    }
}
