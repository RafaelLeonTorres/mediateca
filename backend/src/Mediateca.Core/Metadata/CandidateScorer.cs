using Mediateca.Core.Media;

namespace Mediateca.Core.Metadata;

/// <summary>Un resultado con su parecido de título (0 a 1) y su puntaje total, que suma o resta las pistas.</summary>
public sealed record ScoredCandidate(SearchCandidate Candidate, double Similarity, double Score);

/// <summary>Cómo se puntúa: <c>UseFormat</c> es falso en OMDb (la búsqueda ya filtra película o serie).</summary>
public sealed record ScoringOptions(bool UseFormat = true, bool PreferMovie = false);

/// <summary>
/// Elige el resultado de la búsqueda que corresponde a una carpeta (PLAN.md, sección 8).
/// <list type="bullet">
/// <item>El parecido es el mejor entre los nombres de la carpeta (el principal y los de los paréntesis) y los
/// del resultado (romaji, inglés, original y sinónimos), todos limpios con <see cref="SearchNameCleaner"/>.</item>
/// <item>Suma si coinciden la temporada (+0.15) o el cour (+0.08), el año (+0.20 si empieza ese año, +0.12 si
/// sigue en emisión ese año) y el formato (+0.08 si se pide una película y lo es); resta si la temporada o el
/// cour son otros, si el año de la carpeta está lejos (−0.25, o −0.08 si es el año vecino), o si el formato no
/// es el esperado.</item>
/// <item>Sin año ni temporada en la carpeta, prefiere el resultado más reciente (hasta +0.05).</item>
/// <item>Un resultado solo se acepta si su parecido de título llega a <see cref="MinimumSimilarity"/>.</item>
/// </list>
/// </summary>
public static class CandidateScorer
{
    /// <summary>Parecido de título mínimo para aceptar un resultado. Por debajo, la carpeta queda como "no encontrada".</summary>
    public const double MinimumSimilarity = 0.60;

    /// <summary>Parecido con el que ya no hace falta seguir buscando con otras consultas.</summary>
    public const double ConfidentSimilarity = 0.85;

    public static IReadOnlyList<ScoredCandidate> Rank(
        NameHint hint, IReadOnlyList<SearchCandidate> candidates, ScoringOptions? options = null)
    {
        options ??= new ScoringOptions();

        var years = candidates.Where(c => c.Year is not null).Select(c => c.Year!.Value).ToList();
        var spread = years.Count > 0 ? years.Max() - years.Min() : 0;
        var useRecency = hint.Year is null && hint.Season is null && spread > 0;

        var scored = new List<ScoredCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var similarity = 0.0;
            int? candidateSeason = null;
            int? candidatePart = null;
            foreach (var title in candidate.Titles.Append(candidate.Title).Distinct())
            {
                var cleaned = SearchNameCleaner.Clean(title);
                foreach (var query in hint.AllTitles)
                {
                    var current = NameNormalizer.Similarity(query, cleaned.Title);
                    if (current > similarity)
                    {
                        similarity = current;
                        candidateSeason = cleaned.Season;
                        candidatePart = cleaned.Part;
                    }
                }
            }

            var score = similarity;
            if (hint.Season is { } wantedSeason && candidateSeason is { } season)
            {
                score += wantedSeason == season ? 0.15 : -0.15;
            }

            if (hint.Part is { } wantedPart && candidatePart is { } part)
            {
                score += wantedPart == part ? 0.08 : -0.08;
            }

            if (hint.Year is { } wantedYear && candidate.Year is { } startYear)
            {
                var endYear = candidate.EndYear ?? (candidate.Status == ReleaseStatus.Airing ? int.MaxValue : startYear);
                if (startYear == wantedYear)
                {
                    score += 0.20;
                }
                else if (startYear < wantedYear && wantedYear <= endYear)
                {
                    score += 0.12;
                }
                else
                {
                    score += Math.Abs(startYear - wantedYear) == 1 ? -0.08 : -0.25;
                }
            }

            if (options.UseFormat)
            {
                var isMovie = string.Equals(candidate.Format, "MOVIE", StringComparison.OrdinalIgnoreCase);
                var wantsMovie = hint.IsMovie || options.PreferMovie;
                if (wantsMovie)
                {
                    score += isMovie ? 0.08 : -0.10;
                }
                else
                {
                    if (isMovie)
                    {
                        score -= 0.05;
                    }

                    if (candidate.Format is { } format &&
                        (string.Equals(format, "SPECIAL", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(format, "OVA", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(format, "MUSIC", StringComparison.OrdinalIgnoreCase)))
                    {
                        score -= 0.04;
                    }
                }
            }

            if (useRecency && candidate.Year is { } candidateYear)
            {
                score += 0.05 * (candidateYear - years.Min()) / spread;
            }

            scored.Add(new ScoredCandidate(candidate, similarity, score));
        }

        // OrderByDescending es estable: ante un empate, queda primero el que el proveedor puso antes.
        return scored.OrderByDescending(s => s.Score).ThenByDescending(s => s.Similarity).ToList();
    }

    /// <summary>El mejor resultado cuyo parecido de título llega al mínimo, o null si ninguno.</summary>
    public static ScoredCandidate? PickBest(
        NameHint hint, IReadOnlyList<SearchCandidate> candidates, ScoringOptions? options = null) =>
        Rank(hint, candidates, options).FirstOrDefault(s => s.Similarity >= MinimumSimilarity);
}
