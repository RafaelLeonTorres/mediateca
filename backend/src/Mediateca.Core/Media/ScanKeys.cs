namespace Mediateca.Core.Media;

/// <summary>
/// La clave con la que el escáner reconoce a una serie: el título y el año que salen de su carpeta. Se guarda en
/// <see cref="MediaItem.ScanKey"/> porque los metadatos cambian el título y el año que se muestran.
/// </summary>
public static class ScanKeys
{
    public static string Series(string title, int? year) => $"{title.ToLowerInvariant()}|{year}";
}
