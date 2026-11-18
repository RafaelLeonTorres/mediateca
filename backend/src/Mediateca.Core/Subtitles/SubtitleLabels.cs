namespace Mediateca.Core.Subtitles;

/// <summary>
/// Pone la etiqueta en español de cada pista: "Español", "Inglés (forzados)", "Pista 3" si no trae idioma.
/// Las subidas desde el reproductor siempre lo dicen ("Español (subido)") y, sin idioma, se llaman como su
/// archivo. Si dos pistas quedan con la misma etiqueta las distingue, en este orden: por su título
/// ("Inglés – SDH"), por su origen ("Español (incrustados)" y "Español (archivo)") y, si todavía son iguales,
/// con un número ("Inglés", "Inglés (2)").
/// </summary>
public static class SubtitleLabels
{
    public static IReadOnlyList<SubtitleTrack> Assign(IReadOnlyList<SubtitleTrack> tracks)
    {
        var labels = new string[tracks.Count];
        for (var i = 0; i < tracks.Count; i++)
        {
            labels[i] = BaseLabel(tracks[i], i);
        }

        // 1. Por título, cuando todas las repetidas traen uno distinto.
        foreach (var group in DuplicateGroups(labels))
        {
            var titles = group.Select(i => tracks[i].Title).ToList();
            if (titles.All(title => !string.IsNullOrWhiteSpace(title)) &&
                titles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == titles.Count)
            {
                foreach (var i in group)
                {
                    labels[i] = $"{labels[i]} – {tracks[i].Title}";
                }
            }
        }

        // 2. Por origen, cuando las repetidas vienen de lugares distintos.
        foreach (var group in DuplicateGroups(labels))
        {
            if (group.Select(i => tracks[i].Origin).Distinct().Count() > 1)
            {
                foreach (var i in group)
                {
                    labels[i] = $"{labels[i]} ({OriginName(tracks[i].Origin)})";
                }
            }
        }

        // 3. Con un número: la primera queda como está.
        foreach (var group in DuplicateGroups(labels))
        {
            var count = 1;
            foreach (var i in group)
            {
                if (count++ > 1)
                {
                    labels[i] = $"{labels[i]} ({count - 1})";
                }
            }
        }

        return [.. tracks.Select((track, i) => track with { Label = labels[i] })];
    }

    private static string BaseLabel(SubtitleTrack track, int index)
    {
        var uploaded = track.Origin == SubtitleOrigin.Uploaded;
        var name = track.Language is { Length: > 0 } language
            ? SubtitleLanguages.NameOf(language)
            : uploaded && !string.IsNullOrWhiteSpace(track.Title) ? track.Title.Trim() : $"Pista {index + 1}";

        // "Español (forzados)", "Español (subido)" o las dos cosas juntas: "Español (forzados, subido)".
        var notes = new List<string>(2);
        if (track.IsForced)
        {
            notes.Add("forzados");
        }

        if (uploaded)
        {
            notes.Add("subido");
        }

        return notes.Count == 0 ? name : $"{name} ({string.Join(", ", notes)})";
    }

    private static string OriginName(SubtitleOrigin origin) => origin switch
    {
        SubtitleOrigin.Embedded => "incrustados",
        SubtitleOrigin.External => "archivo",
        _ => "subido",
    };

    /// <summary>Posiciones que comparten etiqueta, en grupos de dos o más, en orden.</summary>
    private static List<List<int>> DuplicateGroups(string[] labels) =>
        [.. labels
            .Select((label, index) => (label, index))
            .GroupBy(pair => pair.label, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Select(pair => pair.index).ToList())];
}
