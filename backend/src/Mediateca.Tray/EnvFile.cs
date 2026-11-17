namespace Mediateca.Tray;

/// <summary>Lectura mínima del archivo <c>.env</c> de la carpeta del compose.</summary>
public static class EnvFile
{
    /// <summary>Puerto por defecto de la web (el mismo que usa docker-compose.yml).</summary>
    public const int DefaultWebPort = 8080;

    /// <summary>
    /// Busca el valor de una variable en líneas <c>CLAVE=VALOR</c>. Ignora líneas vacías y comentarios (<c>#</c>),
    /// acepta el prefijo <c>export</c>, quita comillas simples o dobles alrededor del valor y, en valores sin
    /// comillas, descarta el comentario que sigue a un espacio y <c>#</c>. Si la clave se repite, gana la última.
    /// </summary>
    /// <param name="lines">Líneas del archivo.</param>
    /// <param name="key">Nombre de la variable (distingue mayúsculas).</param>
    /// <returns>El valor, o <c>null</c> si la variable no está.</returns>
    public static string? GetValue(IEnumerable<string> lines, string key)
    {
        string? found = null;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line.Substring(7).TrimStart();
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var name = line.Substring(0, equals).Trim();
            if (!string.Equals(name, key, StringComparison.Ordinal))
            {
                continue;
            }

            found = CleanValue(line.Substring(equals + 1));
        }

        return found;
    }

    /// <summary>Interpreta un valor de puerto; devuelve el predeterminado si no es un número entre 1 y 65535.</summary>
    /// <param name="value">Texto del valor; puede ser nulo.</param>
    /// <param name="defaultPort">Puerto a usar si el valor no sirve.</param>
    /// <returns>El puerto.</returns>
    public static int ParsePort(string? value, int defaultPort)
    {
        if (int.TryParse(value, out var port) && port >= 1 && port <= 65535)
        {
            return port;
        }

        return defaultPort;
    }

    /// <summary>Lee <c>WEB_PORT</c> del <c>.env</c> de la carpeta; 8080 si no existe el archivo o la variable.</summary>
    /// <param name="composeFolder">Carpeta con docker-compose.yml y .env.</param>
    /// <returns>El puerto de la web.</returns>
    public static int ReadWebPort(string composeFolder)
    {
        try
        {
            var path = Path.Combine(composeFolder, ".env");
            if (!File.Exists(path))
            {
                return DefaultWebPort;
            }

            var value = GetValue(File.ReadAllLines(path), "WEB_PORT");
            return ParsePort(value, DefaultWebPort);
        }
        catch (IOException)
        {
            return DefaultWebPort;
        }
        catch (UnauthorizedAccessException)
        {
            return DefaultWebPort;
        }
    }

    private static string CleanValue(string raw)
    {
        var value = raw.Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\''))
        {
            var quote = value[0];
            var closing = value.IndexOf(quote, 1);
            if (closing > 0)
            {
                return value.Substring(1, closing - 1);
            }
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0)
        {
            value = value.Substring(0, comment).TrimEnd();
        }

        return value;
    }
}
