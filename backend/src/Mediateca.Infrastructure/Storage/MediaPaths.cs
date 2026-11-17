namespace Mediateca.Infrastructure.Storage;

/// <summary>
/// Comparación de rutas. Windows y macOS no distinguen mayúsculas en sus discos; Linux sí. La
/// comparación es por carpetas completas: "/media2" no está dentro de "/media".
/// </summary>
public static class MediaPaths
{
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>Ruta completa, sin "..", sin separador final (salvo la raíz del disco).</summary>
    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool AreSame(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), Comparison);

    /// <summary>La ruta es la carpeta o está dentro de ella, a cualquier profundidad.</summary>
    public static bool IsWithin(string path, string folder)
    {
        var normalizedPath = Normalize(path);
        var normalizedFolder = Normalize(folder);
        if (string.Equals(normalizedPath, normalizedFolder, Comparison))
        {
            return true;
        }

        var prefix = Path.EndsInDirectorySeparator(normalizedFolder)
            ? normalizedFolder
            : normalizedFolder + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, Comparison);
    }

    /// <summary>"Carpeta oculta": empieza con punto o el sistema de archivos la marca como oculta o de sistema.</summary>
    public static bool IsHidden(DirectoryInfo directory) =>
        directory.Name.StartsWith('.') ||
        (directory.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
}
