using Mediateca.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Libraries;

/// <summary>
/// Una carpeta que se puede elegir para una biblioteca. <c>Path</c> es la ruta del contenedor (la que se
/// manda al servidor) y <c>DisplayPath</c> la que se muestra: F:\Peliculas en lugar de /discos/F/Peliculas.
/// </summary>
public sealed record FolderEntry(string Name, string Path, string DisplayPath);

/// <summary>
/// Contenido de una carpeta. Sin <c>Path</c> es la lista de carpetas de medios y de discos; <c>Parent</c>
/// es la carpeta de arriba, o null cuando lo de arriba es esa lista.
/// </summary>
public sealed record FolderListing(string? Path, string? DisplayPath, string? Parent, IReadOnlyList<FolderEntry> Folders);

/// <summary>Resultado de explorar carpetas: el listado o un mensaje de error en español.</summary>
public sealed record FolderBrowseResult(FolderListing? Listing, string? Error);

/// <summary>
/// Explora las carpetas del servidor, solo dentro de las carpetas de medios y de los discos montados, para
/// el selector del administrador.
/// </summary>
public sealed class FolderBrowser(IOptions<StorageOptions> storage)
{
    public FolderBrowseResult Browse(string? path)
    {
        var roots = storage.Value.MediaRoots;

        if (string.IsNullOrWhiteSpace(path))
        {
            return ListRoots();
        }

        string fullPath;
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return Fail("La ruta no es absoluta.");
            }

            fullPath = MediaPaths.Normalize(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Fail("La ruta no es válida.");
        }

        // La carpeta de los discos no se elige como tal: arriba de un disco está la lista inicial.
        if (IsDrivesRoot(fullPath))
        {
            return ListRoots();
        }

        var root = roots.FirstOrDefault(r => MediaPaths.IsWithin(fullPath, r));
        if (root is null)
        {
            return Fail("La carpeta está fuera de las carpetas de medios.");
        }

        if (!Directory.Exists(fullPath))
        {
            return Fail("La carpeta no existe o no se puede leer.");
        }

        var folders = new List<FolderEntry>();
        try
        {
            foreach (var directory in new DirectoryInfo(fullPath).EnumerateDirectories())
            {
                if (!MediaPaths.IsHidden(directory))
                {
                    folders.Add(new FolderEntry(directory.Name, directory.FullName, DisplayPathOf(directory.FullName)));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Fail("No hay permiso para leer esa carpeta.");
        }

        folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // Subir desde una carpeta de medios o desde un disco lleva a la lista inicial (Parent null).
        var parentPath = Path.GetDirectoryName(fullPath);
        var isTopLevel = MediaPaths.AreSame(fullPath, root) || (parentPath is not null && IsDrivesRoot(parentPath));
        return new FolderBrowseResult(
            new FolderListing(fullPath, DisplayPathOf(fullPath), isTopLevel ? null : parentPath, folders),
            null);
    }

    // Lista inicial: las carpetas de medios que existen (menos la de los discos) y, después, cada disco montado.
    private FolderBrowseResult ListRoots()
    {
        var folders = new List<FolderEntry>();

        foreach (var root in storage.Value.MediaRoots)
        {
            if (IsDrivesRoot(root) || !Directory.Exists(root))
            {
                continue;
            }

            folders.Add(new FolderEntry(NameOfRoot(root), root, DisplayPathOf(root)));
        }

        var drives = new List<(bool IsDisk, FolderEntry Entry)>();
        try
        {
            if (Directory.Exists(storage.Value.DrivesPath))
            {
                foreach (var directory in new DirectoryInfo(storage.Value.DrivesPath).EnumerateDirectories())
                {
                    var isLetter = directory.Name.Length == 1 && char.IsAsciiLetter(directory.Name[0]);
                    var name = isLetter ? $"Disco {char.ToUpperInvariant(directory.Name[0])}:" : directory.Name;
                    drives.Add((isLetter, new FolderEntry(name, directory.FullName, DisplayPathOf(directory.FullName))));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Sin permiso para leer la carpeta de los discos: la lista queda sin ellos.
        }

        // Primero los discos (C, D...) y después las demás carpetas de la carpeta de los discos.
        folders.AddRange(drives
            .OrderBy(drive => drive.IsDisk ? 0 : 1)
            .ThenBy(drive => drive.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(drive => drive.Entry));
        return new FolderBrowseResult(new FolderListing(null, null, null, folders), null);
    }

    /// <summary>Nombre de una carpeta de medios en la lista inicial: /media es "Carpeta de medios"; las demás, su ruta.</summary>
    public static string NameOfRoot(string root) =>
        MediaPaths.AreSame(root, StorageOptions.DefaultMediaRoot) ? "Carpeta de medios" : root;

    private bool IsDrivesRoot(string path) =>
        !string.IsNullOrWhiteSpace(storage.Value.DrivesPath) && MediaPaths.AreSame(path, storage.Value.DrivesPath);

    private string DisplayPathOf(string path) => MediaPaths.ToDisplayPath(path, storage.Value.DrivesPath);

    private static FolderBrowseResult Fail(string error) => new(null, error);
}
