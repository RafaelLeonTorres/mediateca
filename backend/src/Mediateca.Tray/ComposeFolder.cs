using System.Text.Json;

namespace Mediateca.Tray;

/// <summary>Encuentra la carpeta que contiene docker-compose.yml de Mediateca.</summary>
public static class ComposeFolder
{
    private const string ComposeFileName = "docker-compose.yml";

    /// <summary>Ruta de <c>%LOCALAPPDATA%\Mediateca\bandeja.json</c>, donde se recuerda la carpeta.</summary>
    public static string ConfigPath
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Mediateca", "bandeja.json");
        }
    }

    /// <summary>
    /// Resuelve la carpeta en este orden: argumento <c>--carpeta</c> (y lo guarda en bandeja.json),
    /// bandeja.json, y buscar hacia arriba desde la carpeta del ejecutable.
    /// </summary>
    /// <param name="args">Argumentos de la línea de comandos.</param>
    /// <returns>La carpeta, o <c>null</c> si ninguna contiene docker-compose.yml.</returns>
    public static string? Resolve(string[] args)
    {
        var fromArgument = GetArgumentValue(args, "--carpeta");
        if (!string.IsNullOrWhiteSpace(fromArgument))
        {
            var folder = Normalize(fromArgument);
            if (HasComposeFile(folder))
            {
                TrySaveConfig(folder);
                return folder;
            }
        }

        var saved = ReadConfig();
        if (saved is not null && HasComposeFile(saved))
        {
            return saved;
        }

        return FindUpwards(AppContext.BaseDirectory);
    }

    /// <summary>Devuelve el valor que sigue a la opción (por ejemplo <c>--carpeta "C:\x"</c>); sin distinguir mayúsculas.</summary>
    /// <param name="args">Argumentos de la línea de comandos.</param>
    /// <param name="name">Nombre de la opción, con los guiones.</param>
    /// <returns>El valor, o <c>null</c> si la opción no está o no tiene valor.</returns>
    public static string? GetArgumentValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>Quita comillas y separadores finales y la vuelve ruta absoluta (sin tocar una raíz como <c>C:\</c>).</summary>
    /// <param name="path">Ruta original.</param>
    /// <returns>La ruta normalizada; si no es una ruta válida, la original recortada.</returns>
    public static string Normalize(string path)
    {
        var trimmed = path.Trim().Trim('"');
        try
        {
            var full = Path.GetFullPath(trimmed);
            var root = Path.GetPathRoot(full);
            if (full.Length > (root?.Length ?? 0))
            {
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return full;
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
        catch (NotSupportedException)
        {
            return trimmed;
        }
    }

    /// <summary>Indica si la carpeta contiene docker-compose.yml.</summary>
    /// <param name="folder">Carpeta a revisar.</param>
    /// <returns><c>true</c> si el archivo existe.</returns>
    public static bool HasComposeFile(string folder)
    {
        try
        {
            return Directory.Exists(folder) && File.Exists(Path.Combine(folder, ComposeFileName));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Sube por los directorios padre desde <paramref name="startDirectory"/> hasta encontrar docker-compose.yml.</summary>
    /// <param name="startDirectory">Carpeta donde empezar.</param>
    /// <returns>La carpeta que lo contiene, o <c>null</c>.</returns>
    public static string? FindUpwards(string startDirectory)
    {
        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(startDirectory);
        }
        catch (ArgumentException)
        {
            return null;
        }

        while (current is not null)
        {
            if (HasComposeFile(current.FullName))
            {
                return Normalize(current.FullName);
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? ReadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("carpeta", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : Normalize(text);
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static void TrySaveConfig(string folder)
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["carpeta"] = folder });
            File.WriteAllText(ConfigPath, json);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
