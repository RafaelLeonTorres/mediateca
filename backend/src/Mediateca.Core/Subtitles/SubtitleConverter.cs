using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Mediateca.Core.Subtitles;

/// <summary>
/// Convierte subtítulos de texto a WebVTT, que es lo que entiende el navegador, sin depender de FFmpeg:
/// SRT (BOM, saltos de línea de Windows, la coma de los tiempos, índices) y ASS/SSA (las líneas
/// <c>Dialogue:</c>, sin las etiquetas <c>{\...}</c>). El navegador no necesita los estilos del ASS: el reproductor
/// dibuja todos los subtítulos con el mismo estilo.
/// </summary>
public static partial class SubtitleConverter
{
    /// <summary>Hora de SRT o de WebVTT: <c>00:01:02,345</c> o <c>0:01:02.34</c> (horas, minutos, segundos y fracción).</summary>
    [GeneratedRegex(@"^\s*(\d+):(\d{2}):(\d{2})[,.](\d{1,3})\s*-->\s*(\d+):(\d{2}):(\d{2})[,.](\d{1,3})")]
    private static partial Regex SrtTiming();

    /// <summary>Hora de ASS: <c>H:MM:SS.cc</c>.</summary>
    [GeneratedRegex(@"^\s*(\d+):(\d{2}):(\d{2})[.,](\d{1,3})\s*$")]
    private static partial Regex AssTime();

    /// <summary>Etiquetas de estilo de SSA que a veces aparecen en los SRT: <c>{\an8}</c>.</summary>
    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Braces();

    [GeneratedRegex(@"</?font[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex FontTags();

    /// <summary>Un "&lt;" que no abre ni cierra una de las etiquetas que WebVTT conoce (i, b, u).</summary>
    [GeneratedRegex(@"<(?!/?[ibu]>)")]
    private static partial Regex StrayLessThan();

    [GeneratedRegex(@"\{[^}]*\\p[1-9][^}]*\}")]
    private static partial Regex DrawingMode();

    private const string Header = "WEBVTT";

    /// <summary>
    /// Texto de un archivo de subtítulos. Respeta el BOM de UTF-8 y de UTF-16; sin BOM lo lee como UTF-8 y, si
    /// no es UTF-8 válido (los SRT viejos suelen estar en Windows-1252), como Latin-1, que conserva las
    /// letras con acento y la eñe del español.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>SRT a WebVTT: sin BOM ni índices, con punto en los tiempos y saltos de línea normales.</summary>
    public static string SrtToVtt(string? srt)
    {
        var builder = new StringBuilder(Header).Append("\n\n");
        if (string.IsNullOrWhiteSpace(srt))
        {
            return builder.ToString();
        }

        var lines = NormalizeLines(srt).Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var timing = SrtTiming().Match(lines[i]);
            if (!timing.Success)
            {
                // Un índice, una línea en blanco o basura entre cues.
                i++;
                continue;
            }

            var start = FormatTime(timing.Groups[1].Value, timing.Groups[2].Value, timing.Groups[3].Value, timing.Groups[4].Value);
            var end = FormatTime(timing.Groups[5].Value, timing.Groups[6].Value, timing.Groups[7].Value, timing.Groups[8].Value);
            i++;

            var text = new List<string>();
            while (i < lines.Length && lines[i].Trim().Length > 0)
            {
                // Un SRT sin la línea en blanco: el índice del siguiente cue y su hora.
                if (i + 1 < lines.Length && SrtTiming().IsMatch(lines[i + 1]) && lines[i].Trim().All(char.IsAsciiDigit))
                {
                    break;
                }

                text.Add(CleanSrtLine(lines[i]));
                i++;
            }

            var body = string.Join('\n', text.Where(line => line.Length > 0));
            if (body.Length > 0)
            {
                builder.Append(start).Append(" --> ").Append(end).Append('\n').Append(body).Append("\n\n");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// ASS o SSA a WebVTT: toma las líneas <c>Dialogue:</c> de <c>[Events]</c> según su línea <c>Format:</c>, quita las
    /// etiquetas <c>{\...}</c>, convierte <c>\N</c> en salto de línea y ordena los cues por hora de inicio. Los
    /// dibujos vectoriales (<c>\p1</c>) y los cues vacíos se omiten.
    /// </summary>
    public static string AssToVtt(string? ass)
    {
        var builder = new StringBuilder(Header).Append("\n\n");
        if (string.IsNullOrWhiteSpace(ass))
        {
            return builder.ToString();
        }

        // Si el archivo no trae Format, estas son las posiciones de los formatos estándar de ASS y SSA.
        int startColumn = 1, endColumn = 2, textColumn = 9, columns = 10;
        var inEvents = false;
        var cues = new List<(long Start, long End, string Text)>();

        foreach (var rawLine in NormalizeLines(ass).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEvents)
            {
                continue;
            }

            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                var names = line["Format:".Length..].Split(',').Select(name => name.Trim().ToLowerInvariant()).ToList();
                var start = names.IndexOf("start");
                var end = names.IndexOf("end");
                var text = names.IndexOf("text");
                if (start >= 0 && end >= 0 && text >= 0)
                {
                    (startColumn, endColumn, textColumn, columns) = (start, end, text, names.Count);
                }

                continue;
            }

            if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // El texto es la última columna y puede traer comas: se parte solo hasta ahí.
            var fields = line["Dialogue:".Length..].TrimStart().Split(',', columns);
            if (fields.Length <= Math.Max(textColumn, Math.Max(startColumn, endColumn)))
            {
                continue;
            }

            var startMs = ParseAssTime(fields[startColumn]);
            var endMs = ParseAssTime(fields[endColumn]);
            var raw = fields[textColumn];
            if (startMs < 0 || endMs <= startMs || DrawingMode().IsMatch(raw))
            {
                continue;
            }

            var cleaned = CleanAssText(raw);
            if (cleaned.Length > 0)
            {
                cues.Add((startMs, endMs, cleaned));
            }
        }

        foreach (var (start, end, text) in cues.OrderBy(cue => cue.Start))
        {
            builder.Append(FormatMilliseconds(start)).Append(" --> ").Append(FormatMilliseconds(end))
                .Append('\n').Append(text).Append("\n\n");
        }

        return builder.ToString();
    }

    private static string NormalizeLines(string text) =>
        text.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string CleanSrtLine(string line)
    {
        var text = Braces().Replace(line, string.Empty);
        text = FontTags().Replace(text, string.Empty);
        return StrayLessThan().Replace(text, "&lt;").Trim();
    }

    private static string CleanAssText(string raw)
    {
        var text = Braces().Replace(raw, string.Empty)
            .Replace("\\N", "\n", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace("\\h", " ", StringComparison.Ordinal);

        // El texto de un cue de WebVTT se lee como HTML mínimo: los símbolos se escapan.
        text = text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
        return string.Join('\n', text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
    }

    /// <summary>Milisegundos de una hora de ASS, o -1 si no es una hora.</summary>
    private static long ParseAssTime(string value)
    {
        var match = AssTime().Match(value);
        return match.Success
            ? ToMilliseconds(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value)
            : -1;
    }

    private static long ToMilliseconds(string hours, string minutes, string seconds, string fraction)
    {
        var milliseconds = int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        return ((long.Parse(hours, CultureInfo.InvariantCulture) * 60 + long.Parse(minutes, CultureInfo.InvariantCulture)) * 60 +
                long.Parse(seconds, CultureInfo.InvariantCulture)) * 1000 + milliseconds;
    }

    private static string FormatTime(string hours, string minutes, string seconds, string fraction) =>
        FormatMilliseconds(ToMilliseconds(hours, minutes, seconds, fraction));

    private static string FormatMilliseconds(long total)
    {
        var milliseconds = total % 1000;
        var seconds = total / 1000 % 60;
        var minutes = total / 60_000 % 60;
        var hours = total / 3_600_000;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}.{milliseconds:000}");
    }
}
