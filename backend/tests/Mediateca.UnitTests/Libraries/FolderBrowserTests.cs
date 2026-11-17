using Mediateca.Infrastructure.Libraries;
using Mediateca.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mediateca.UnitTests.Libraries;

public sealed class FolderBrowserTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"mediateca-unit-{Guid.NewGuid():N}");
    private readonly string _media;
    private readonly FolderBrowser _browser;

    public FolderBrowserTests()
    {
        _media = MediaPaths.Normalize(Path.Combine(_temp, "media"));
        Directory.CreateDirectory(Path.Combine(_media, "Series"));
        Directory.CreateDirectory(Path.Combine(_media, "Peliculas", "Accion"));
        Directory.CreateDirectory(Path.Combine(_media, ".oculta"));
        Directory.CreateDirectory(Path.Combine(_temp, "afuera"));

        _browser = new FolderBrowser(Options.Create(new StorageOptions { MediaRoots = [_media] }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    [Fact]
    public void Sin_ruta_lista_las_carpetas_de_medios()
    {
        var resultado = _browser.Browse(null);

        Assert.Null(resultado.Error);
        Assert.Null(resultado.Listing!.Path);
        Assert.Equal(new[] { _media }, resultado.Listing.Folders.Select(f => f.Path).ToArray());
    }

    [Fact]
    public void Lista_las_subcarpetas_en_orden_y_sin_las_ocultas()
    {
        var resultado = _browser.Browse(_media);

        Assert.Equal(new[] { "Peliculas", "Series" }, resultado.Listing!.Folders.Select(f => f.Name).ToArray());
        Assert.Equal(_media, resultado.Listing.Path);
        // Subir desde la carpeta de medios lleva a la lista de carpetas de medios.
        Assert.Null(resultado.Listing.Parent);
    }

    [Fact]
    public void Indica_la_carpeta_de_arriba_dentro_de_la_de_medios()
    {
        var peliculas = Path.Combine(_media, "Peliculas");

        var resultado = _browser.Browse(peliculas);

        Assert.Equal(_media, resultado.Listing!.Parent);
        Assert.Equal(new[] { "Accion" }, resultado.Listing.Folders.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void Rechaza_las_carpetas_fuera_de_las_de_medios()
    {
        var afuera = _browser.Browse(Path.Combine(_temp, "afuera"));
        var puntos = _browser.Browse(Path.Combine(_media, "..", "afuera"));

        Assert.Null(afuera.Listing);
        Assert.NotNull(afuera.Error);
        Assert.Null(puntos.Listing);
    }

    [Fact]
    public void Rechaza_rutas_relativas_y_carpetas_que_no_existen()
    {
        Assert.NotNull(_browser.Browse("Peliculas").Error);
        Assert.NotNull(_browser.Browse(Path.Combine(_media, "NoExiste")).Error);
    }
}
