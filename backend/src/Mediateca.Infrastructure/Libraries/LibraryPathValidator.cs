using Mediateca.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Libraries;

/// <summary>Resultado de validar las carpetas de una biblioteca: rutas normalizadas o mensajes de error.</summary>
public sealed record LibraryPathsValidation(IReadOnlyList<string> Paths, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Valida las carpetas de una biblioteca. El cliente es quien manda las rutas, así que cada una tiene
/// que ser absoluta, existir y estar dentro de una carpeta de medios (<c>Storage:MediaRoots</c>); sin
/// repetidas ni anidadas dentro de la misma biblioteca.
/// </summary>
public sealed class LibraryPathValidator(IOptions<StorageOptions> storage)
{
    /// <summary>Máximo de carpetas por biblioteca.</summary>
    public const int MaxPaths = 20;

    public LibraryPathsValidation Validate(IEnumerable<string>? paths)
    {
        var errors = new List<string>();
        var normalized = new List<string>();

        var requested = (paths ?? Array.Empty<string>()).ToList();
        if (requested.Count == 0)
        {
            errors.Add("Agrega al menos una carpeta.");
            return new LibraryPathsValidation(normalized, errors);
        }

        if (requested.Count > MaxPaths)
        {
            errors.Add($"Una biblioteca puede tener hasta {MaxPaths} carpetas.");
            return new LibraryPathsValidation(normalized, errors);
        }

        var roots = storage.Value.MediaRoots;
        foreach (var path in requested)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                errors.Add("Hay una carpeta vacía. Elígela o quítala de la lista.");
                continue;
            }

            var trimmed = path.Trim();
            if (trimmed.Contains('\0') || !Path.IsPathFullyQualified(trimmed))
            {
                errors.Add($"La carpeta '{trimmed}' no es una ruta absoluta. Elígela con el selector de carpetas.");
                continue;
            }

            string fullPath;
            try
            {
                fullPath = MediaPaths.Normalize(trimmed);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"La carpeta '{trimmed}' no es una ruta válida.");
                continue;
            }

            if (!roots.Any(root => MediaPaths.IsWithin(fullPath, root)))
            {
                errors.Add(
                    $"La carpeta '{fullPath}' está fuera de las carpetas de medios ({string.Join(", ", roots)}). " +
                    "Elige una carpeta dentro de ellas.");
                continue;
            }

            if (!Directory.Exists(fullPath))
            {
                errors.Add($"La carpeta '{fullPath}' no existe o no se puede leer.");
                continue;
            }

            normalized.Add(fullPath);
        }

        // Repetidas o anidadas: se compara cada par una sola vez.
        for (var i = 0; i < normalized.Count; i++)
        {
            for (var j = i + 1; j < normalized.Count; j++)
            {
                if (MediaPaths.AreSame(normalized[i], normalized[j]))
                {
                    errors.Add($"La carpeta '{normalized[j]}' está repetida.");
                }
                else if (MediaPaths.IsWithin(normalized[j], normalized[i]) || MediaPaths.IsWithin(normalized[i], normalized[j]))
                {
                    errors.Add($"Las carpetas '{normalized[i]}' y '{normalized[j]}' se contienen una a la otra. Deja solo la más externa.");
                }
            }
        }

        return new LibraryPathsValidation(normalized, errors);
    }
}
