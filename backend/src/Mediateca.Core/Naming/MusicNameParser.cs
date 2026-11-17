using System.Globalization;
using System.Text.RegularExpressions;

namespace Mediateca.Core.Naming;

/// <summary>Datos de una pista que salen de su ruta, para cuando el archivo no tiene etiquetas.</summary>
public sealed record ParsedTrack(
    string? Artist,
    string? Album,
    int? Year,
    int? DiscNumber,
    int? TrackNumber,
    string Title);

/// <summary>
/// Música: <c>Artista/Álbum (Año)/NN - Canción.ext</c>. Recibe la ruta relativa a la carpeta de la
/// biblioteca. Acepta un prefijo de disco ("1-03 - Canción") y carpetas "CD1" o "Disc 2".
/// </summary>
public static class MusicNameParser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex DiscFolder = new(@"^(?:cd|disc|disco|dvd)[\s._-]*([0-9]{1,2})$", Options);

    private static readonly Regex YearInBrackets = new(@"[\(\[]\s*((?:19[0-9]{2}|20[0-3][0-9]))\s*[\)\]]", Options);

    private static readonly Regex LeadingYear = new(@"^((?:19[0-9]{2}|20[0-3][0-9]))\s+-\s+(.+)$", Options);

    // "1-03 - Canción": disco y pista.
    private static readonly Regex DiscAndTrack = new(
        @"^([0-9]{1,2})-([0-9]{1,3})(?=[\s.\-_])\s*[-._]?\s*(.+)$", Options);

    // "01 - Canción", "01. Canción", "01) Canción".
    private static readonly Regex TrackWithSeparator = new(@"^([0-9]{1,3})\s*[-._)]\s*(.+)$", Options);

    // "01 Canción": solo con dos cifras, para no tomar "7 Years" o "1979" por una pista.
    private static readonly Regex TrackWithSpace = new(@"^([0-9]{2})\s+(.+)$", Options);

    public static ParsedTrack Parse(string relativePath)
    {
        var segments = PathSegments.Split(relativePath);
        var directories = segments[..^1].ToList();
        var fileStem = PathSegments.Stem(segments[^1]).Replace('_', ' ');

        int? disc = null;
        if (directories.Count > 0 && IsDiscFolder(directories[^1], out var folderDisc))
        {
            disc = folderDisc;
            directories.RemoveAt(directories.Count - 1);
        }

        string? artist = null;
        string? album = null;
        int? year = null;
        if (directories.Count >= 2)
        {
            artist = NullIfEmpty(TitleParsing.Collapse(directories[0].Replace('_', ' ')));
            (album, year) = ParseAlbumFolder(directories[1]);
        }
        else if (directories.Count == 1)
        {
            (album, year) = ParseAlbumFolder(directories[0]);
        }

        int? track = null;
        var title = TitleParsing.Collapse(fileStem);
        Match match;
        if ((match = DiscAndTrack.Match(title)).Success)
        {
            disc = Number(match.Groups[1]);
            track = Number(match.Groups[2]);
            title = TitleParsing.Collapse(match.Groups[3].Value);
        }
        else if ((match = TrackWithSeparator.Match(title)).Success)
        {
            track = Number(match.Groups[1]);
            title = TitleParsing.Collapse(match.Groups[2].Value);
        }
        else if ((match = TrackWithSpace.Match(title)).Success)
        {
            track = Number(match.Groups[1]);
            title = TitleParsing.Collapse(match.Groups[2].Value);
        }

        return new ParsedTrack(artist, album, year, disc, track, title);
    }

    /// <summary>Indica si la carpeta separa los discos de un álbum ("CD1", "Disc 2", "Disco 1").</summary>
    public static bool IsDiscFolder(string folderName, out int disc)
    {
        ArgumentNullException.ThrowIfNull(folderName);

        var match = DiscFolder.Match(folderName.Trim());
        disc = match.Success ? Number(match.Groups[1]) : 0;
        return match.Success;
    }

    /// <summary>"Álbum (2020)", "[2019] Álbum", "2018 - Álbum"; los corchetes de formato ([FLAC]) se ignoran.</summary>
    private static (string? Title, int? Year) ParseAlbumFolder(string name)
    {
        var text = name.Replace('_', ' ');
        int? year = null;

        var inBrackets = YearInBrackets.Match(text);
        if (inBrackets.Success)
        {
            year = Number(inBrackets.Groups[1]);
            text = text[..inBrackets.Index] + " " + text[(inBrackets.Index + inBrackets.Length)..];
        }
        else
        {
            var leading = LeadingYear.Match(text);
            if (leading.Success)
            {
                year = Number(leading.Groups[1]);
                text = leading.Groups[2].Value;
            }
        }

        var title = TitleParsing.Collapse(TitleParsing.TrimEdges(TitleParsing.Brackets.Replace(text, " ")));
        return (NullIfEmpty(title), year);
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);
}
