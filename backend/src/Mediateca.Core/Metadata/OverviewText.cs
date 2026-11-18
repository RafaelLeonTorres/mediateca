using System.Net;
using System.Text.RegularExpressions;

namespace Mediateca.Core.Metadata;

/// <summary>
/// Convierte el resumen que entrega AniList (con HTML: <c>&lt;br&gt;</c>, <c>&lt;i&gt;</c>…) en texto plano
/// con saltos de línea, y quita la línea final "(Source: …)" que agregan algunos resúmenes.
/// </summary>
public static class OverviewText
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex LineBreak = new(@"<br\s*/?>", Options);
    private static readonly Regex ParagraphEnd = new(@"</p\s*>", Options);
    private static readonly Regex Tags = new(@"<[^>]*>", Options);
    private static readonly Regex TrailingSource = new(@"(?:\n|^)\s*\((?:source|fuente):[^)]*\)\s*$", Options);
    private static readonly Regex SpacesAroundBreaks = new(@"[ \t]*\n[ \t]*", Options);
    private static readonly Regex ManyBreaks = new(@"\n{3,}", Options);
    private static readonly Regex Spaces = new(@"[ \t]{2,}", Options);

    /// <summary>El resumen como texto, o null si queda vacío.</summary>
    public static string? FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = html.Replace("\r\n", "\n").Replace('\r', '\n');
        text = LineBreak.Replace(text, "\n");
        text = ParagraphEnd.Replace(text, "\n\n");
        text = Tags.Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        text = SpacesAroundBreaks.Replace(text, "\n");
        text = Spaces.Replace(text, " ");
        text = ManyBreaks.Replace(text, "\n\n").Trim();
        text = TrailingSource.Replace(text, "").Trim();

        return text.Length == 0 ? null : text;
    }
}
