using System.Globalization;
using System.Text.RegularExpressions;

namespace Mediateca.Core.Naming;

/// <summary>Nombre sin las etiquetas de identificador, y los identificadores que traía.</summary>
public sealed record TaggedName(string Name, string? ImdbId, int? AniDbId);

/// <summary>
/// Etiquetas opcionales en el nombre de una carpeta o de un archivo para no depender de la
/// búsqueda: <c>[imdbid-tt0118799]</c> y <c>[anidbid-23]</c> (PLAN.md, sección 7).
/// </summary>
public static class FolderTags
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex Imdb = new(
        @"[\[\{\(]\s*imdb(?:id)?\s*[-=]\s*(tt[0-9]{5,10})\s*[\]\}\)]", Options);

    private static readonly Regex AniDb = new(
        @"[\[\{\(]\s*anidb(?:id)?\s*[-=]\s*([0-9]{1,9})\s*[\]\}\)]", Options);

    /// <summary>Quita las etiquetas del nombre y devuelve los identificadores que encontró.</summary>
    public static TaggedName Extract(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string? imdbId = null;
        int? aniDbId = null;

        var imdb = Imdb.Match(name);
        if (imdb.Success)
        {
            imdbId = imdb.Groups[1].Value.ToLowerInvariant();
            name = Imdb.Replace(name, " ");
        }

        var aniDb = AniDb.Match(name);
        if (aniDb.Success)
        {
            aniDbId = int.Parse(aniDb.Groups[1].Value, CultureInfo.InvariantCulture);
            name = AniDb.Replace(name, " ");
        }

        return new TaggedName(TitleParsing.Collapse(name), imdbId, aniDbId);
    }
}
