using System.Globalization;
using System.Text.RegularExpressions;

namespace Mediateca.Core.Naming;

/// <summary>Título y año que se leen de un nombre de carpeta o de archivo.</summary>
internal readonly record struct TitleAndYear(string Title, int? Year);

/// <summary>
/// Lógica común de los analizadores: separadores, etiquetas de calidad ("1080p", "x264"…) y año.
/// Son funciones puras, sin acceso al disco.
/// </summary>
internal static class TitleParsing
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Años de 1900 a 2039.
    private const string YearPattern = "(?:19[0-9]{2}|20[0-3][0-9])";

    /// <summary>Grupos entre corchetes: [Grupo], [1080p], [ABCD1234].</summary>
    public static readonly Regex Brackets = new(@"\[[^\]]*\]", Options);

    private static readonly Regex Spaces = new(@"\s+", Options);

    // Primera etiqueta de calidad, códec o audio. Todo lo que sigue deja de ser parte del título.
    private static readonly Regex Release = new(
        @"(?<![\p{L}\p{N}])(?:2160p|1080[pi]|720p|576p|480p|4k|uhd|blu-?ray|bdrip|brrip|bdremux|remux|web-?dl|web-?rip|hdtv|dvdrip|hdrip|[xh][ .]?26[45]|hevc|xvid|aac|ac3|dts(?:-?hd)?|dd[p+]?[ ]?[0-9](?:\.[0-9])?|10bit|hdr(?:10)?)(?![\p{L}])",
        Options);

    private static readonly Regex ParenYear = new(@"[\(\[]\s*(" + YearPattern + @")\s*[\)\]]", Options);

    private static readonly Regex BareYear = new(@"(?<![\p{L}\p{N}])(" + YearPattern + @")(?![\p{L}\p{N}])", Options);

    // Separadores y signos que sobran al inicio o al final de un título.
    private static readonly Regex Edges = new(@"^[\s\-_.(\[:,]+|[\s\-_.(\[:,]+$", Options);

    /// <summary>Colapsa los espacios repetidos y recorta los extremos.</summary>
    public static string Collapse(string text) => Spaces.Replace(text, " ").Trim();

    /// <summary>Quita los separadores y signos sobrantes de los extremos.</summary>
    public static string TrimEdges(string text) => Edges.Replace(text, "");

    /// <summary>
    /// Los puntos separan palabras solo cuando el nombre no tiene espacios ("The.Matrix.1999"); en
    /// "Mr. Robot (2015)" se respetan. Los guiones bajos siempre son espacios.
    /// </summary>
    public static string NormalizeSeparators(string text)
    {
        if (!text.Contains(' '))
        {
            text = text.Replace('.', ' ');
        }

        return text.Replace('_', ' ');
    }

    /// <summary>Corta el texto en la primera etiqueta de calidad, códec o audio.</summary>
    public static string CutRelease(string text)
    {
        var match = Release.Match(text);
        return match.Success ? text[..match.Index] : text;
    }

    /// <summary>Quita corchetes y etiquetas de calidad y recorta los extremos.</summary>
    public static string CleanTitle(string text) =>
        Collapse(TrimEdges(CutRelease(Brackets.Replace(text, " "))));

    /// <summary>
    /// Separa título y año: "Nombre (2008)" o "Nombre [2008]"; si no hay año entre paréntesis, el
    /// último año suelto que no esté al inicio ("The.Matrix.1999.1080p"). Un número al inicio
    /// ("2012") o fuera de rango ("Blade Runner 2049") queda en el título.
    /// </summary>
    public static TitleAndYear ParseTitleAndYear(string raw)
    {
        var text = NormalizeSeparators(raw);

        var parenYear = ParenYear.Match(text);
        if (parenYear.Success)
        {
            var year = int.Parse(parenYear.Groups[1].Value, CultureInfo.InvariantCulture);
            var title = CleanTitle(text[..parenYear.Index]);
            if (title.Length == 0)
            {
                title = CleanTitle(text[(parenYear.Index + parenYear.Length)..]);
            }

            return new TitleAndYear(title, year);
        }

        var cut = CutRelease(Brackets.Replace(text, " "));
        Match? found = null;
        foreach (Match match in BareYear.Matches(cut))
        {
            if (TrimEdges(cut[..match.Index]).Length > 0)
            {
                found = match;
            }
        }

        return found is null
            ? new TitleAndYear(CleanTitle(cut), null)
            : new TitleAndYear(
                CleanTitle(cut[..found.Index]),
                int.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture));
    }
}
