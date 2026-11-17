using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Storage;

/// <summary>Sección "Storage" de la configuración.</summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>
    /// Carpeta de datos de la app: pósters y volcado de títulos de AniDB. Si es relativa, se toma
    /// desde la carpeta de la API. En Docker es /data (el volumen appdata).
    /// </summary>
    public string DataPath { get; set; } = "data";
}

public static class StorageExtensions
{
    /// <summary>Crea la carpeta de datos si no existe y devuelve su ruta completa.</summary>
    public static string EnsureDataFolder(this IServiceProvider services)
    {
        var dataPath = services.GetRequiredService<IOptions<StorageOptions>>().Value.DataPath;
        Directory.CreateDirectory(dataPath);
        return dataPath;
    }
}
