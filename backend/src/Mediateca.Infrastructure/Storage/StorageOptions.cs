using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Storage;

/// <summary>Sección "Storage" de la configuración.</summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Carpeta de medios que se usa cuando la configuración no define ninguna (el montaje de Docker).</summary>
    public const string DefaultMediaRoot = "/media";

    /// <summary>Carpeta de los discos cuando la configuración no define ninguna (el montaje de Docker).</summary>
    public const string DefaultDrivesRoot = "/discos";

    /// <summary>
    /// Carpeta donde Docker monta los discos del equipo, de solo lectura: una subcarpeta por disco
    /// (/discos/C, /discos/F...). setup.ps1 genera los montajes en Windows; en Linux y macOS se escriben
    /// a mano (ver README). Es una carpeta de medios más, así que el selector, el validador y las
    /// imágenes la tratan igual que /media. Si no existe, simplemente no aparece nada.
    /// </summary>
    public string DrivesPath { get; set; } = DefaultDrivesRoot;

    /// <summary>
    /// Carpeta de datos de la app: pósters, carátulas y volcado de títulos de AniDB. Si es relativa,
    /// se toma desde la carpeta de la API. En Docker es /data (el volumen appdata).
    /// </summary>
    public string DataPath { get; set; } = "data";

    /// <summary>
    /// Carpetas dentro de las que pueden estar las bibliotecas. Es un arreglo y no una lista porque el
    /// enlazador de configuración agrega a lo que ya tenga la propiedad: así el valor por defecto
    /// (/media) se aplica después, solo si la configuración no define ninguna. Para cambiarla fuera de
    /// Docker: <c>Storage:MediaRoots:0</c>. Ya normalizadas (rutas completas, sin separador final).
    /// </summary>
    public string[] MediaRoots { get; set; } = [];

    /// <summary>Carpeta de las imágenes que genera la app (carátulas extraídas de las etiquetas, pósters).</summary>
    public string ImagesPath => Path.Combine(DataPath, "images");

    /// <summary>Indica si la ruta está dentro de alguna carpeta de medios (o es una de ellas).</summary>
    public bool IsInsideMediaRoots(string path) =>
        MediaRoots.Any(root => MediaPaths.IsWithin(path, root));
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
