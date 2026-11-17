using System.Text.RegularExpressions;

namespace Mediateca.Core.Naming;

/// <summary>Título y año de una película, y los identificadores que traía el nombre.</summary>
public sealed record ParsedMovie(string Title, int? Year, string? ImdbId, int? AniDbId);

/// <summary>
/// Película: <c>Nombre (Año)/archivo.ext</c> o <c>Nombre (Año).ext</c>. Recibe la ruta relativa a la
/// carpeta de la biblioteca; la primera carpeta identifica la película y, si el archivo está
/// suelto, se usa el nombre del archivo.
/// </summary>
public static class MovieNameParser
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "movie", "film", "video", "pelicula", "película", "main", "feature", "index", "play", "title", "full",
    };

    private static readonly Regex GenericPattern = new(
        @"^(?:cd|disc|disco|part|parte)\s*[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ParsedMovie Parse(string relativePath)
    {
        var segments = PathSegments.Split(relativePath);
        var fileName = segments[^1];
        var file = FolderTags.Extract(PathSegments.Stem(fileName));
        var fileParsed = TitleParsing.ParseTitleAndYear(file.Name);

        var folder = segments.Length > 1 ? FolderTags.Extract(segments[0]) : null;
        var folderParsed = folder is null ? default : TitleParsing.ParseTitleAndYear(folder.Name);

        // La carpeta manda cuando el nombre del archivo es genérico ("movie.mkv") o cuando ella
        // trae el año y el archivo no; si el archivo tiene su propio año y la carpeta no (una
        // colección), manda el archivo.
        var useFolder = folder is not null && folderParsed.Title.Length > 0 &&
            (IsGeneric(fileParsed.Title) || folderParsed.Year is not null);
        var chosen = useFolder ? folderParsed : fileParsed;

        var title = chosen.Title.Length > 0 ? chosen.Title : PathSegments.Stem(fileName);
        return new ParsedMovie(
            title,
            chosen.Year,
            folder?.ImdbId ?? file.ImdbId,
            folder?.AniDbId ?? file.AniDbId);
    }

    private static bool IsGeneric(string title) =>
        title.Length == 0 || GenericNames.Contains(title) || GenericPattern.IsMatch(title);
}
