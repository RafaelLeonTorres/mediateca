using System.Text.RegularExpressions;

namespace Mediateca.Core.Metadata;

/// <summary>Un identificador de proveedor: <c>Provider</c> es "anilist" (id numérico) u "omdb" (id de IMDb, <c>tt0118799</c>).</summary>
public sealed record ParsedIdentifier(string Provider, string Id);

/// <summary>
/// Lo que el administrador pega en Identificar: el id de AniList (<c>21</c>), su dirección
/// (<c>https://anilist.co/anime/21/One-Piece</c>), el id de IMDb (<c>tt0118799</c>) o su dirección
/// (<c>https://www.imdb.com/es/title/tt0118799/</c>).
/// </summary>
public static class IdentifierParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex AniListNumber = new(@"^[0-9]{1,9}$", Options);
    private static readonly Regex ImdbId = new(@"^tt[0-9]{5,10}$", Options);
    private static readonly Regex AniListUrl = new(@"(?:^|[/.])anilist\.co/anime/([0-9]{1,9})(?![0-9])", Options);
    private static readonly Regex ImdbUrl = new(@"(?:^|[/.])imdb\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?title/(tt[0-9]{5,10})(?![0-9])", Options);

    /// <summary>Reconoce el identificador, o devuelve null si no es ninguno de los formatos conocidos.</summary>
    public static ParsedIdentifier? Parse(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (AniListNumber.IsMatch(text))
        {
            return AniListId(text);
        }

        if (ImdbId.IsMatch(text))
        {
            return new ParsedIdentifier(MetadataProviders.Omdb, text.ToLowerInvariant());
        }

        var aniList = AniListUrl.Match(text);
        if (aniList.Success)
        {
            return AniListId(aniList.Groups[1].Value);
        }

        var imdb = ImdbUrl.Match(text);
        return imdb.Success
            ? new ParsedIdentifier(MetadataProviders.Omdb, imdb.Groups[1].Value.ToLowerInvariant())
            : null;
    }

    // Sin ceros a la izquierda; el 0 no es un id.
    private static ParsedIdentifier? AniListId(string digits)
    {
        var id = digits.TrimStart('0');
        return id.Length == 0 ? null : new ParsedIdentifier(MetadataProviders.AniList, id);
    }
}
