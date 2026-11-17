using Mediateca.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Libraries;

/// <summary>Una carpeta que se puede elegir para una biblioteca.</summary>
public sealed record FolderEntry(string Name, string Path);

/// <summary>
/// Contenido de una carpeta. Sin <c>Path</c> es la lista de carpetas de medios; <c>Parent</c> es la
/// carpeta de arriba, o null cuando lo de arriba es la lista de carpetas de medios.
/// </summary>
public sealed record FolderListing(string? Path, string? Parent, IReadOnlyList<FolderEntry> Folders);

/// <summary>Resultado de explorar carpetas: el listado o un mensaje de error en español.</summary>
public sealed record FolderBrowseResult(FolderListing? Listing, string? Error);

/// <summary>Explora las carpetas del servidor, solo dentro de las carpetas de medios, para el selector del administrador.</summary>
public sealed class FolderBrowser(IOptions<StorageOptions> storage)
{
    public FolderBrowseResult Browse(string? path)
    {
        var roots = storage.Value.MediaRoots;

        if (string.IsNullOrWhiteSpace(path))
        {
            var rootFolders = roots
                .Where(Directory.Exists)
                .Select(root => new FolderEntry(root, root))
                .ToList();
            return new FolderBrowseResult(new FolderListing(null, null, rootFolders), null);
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
                    folders.Add(new FolderEntry(directory.Name, directory.FullName));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Fail("No hay permiso para leer esa carpeta.");
        }

        folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // Subir desde una carpeta de medios lleva a la lista de carpetas de medios (Parent null).
        var parent = MediaPaths.AreSame(fullPath, root) ? null : Path.GetDirectoryName(fullPath);
        return new FolderBrowseResult(new FolderListing(fullPath, parent, folders), null);
    }

    private static FolderBrowseResult Fail(string error) => new(null, error);
}
