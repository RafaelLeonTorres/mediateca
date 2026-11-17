namespace Mediateca.Core.Naming;

/// <summary>Divide rutas relativas con cualquiera de los dos separadores (/ y \).</summary>
internal static class PathSegments
{
    private static readonly char[] Separators = ['/', '\\'];

    public static string[] Split(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return relativePath.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Nombre del archivo sin la extensión. Los nombres sin extensión se dejan igual.</summary>
    public static string Stem(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
