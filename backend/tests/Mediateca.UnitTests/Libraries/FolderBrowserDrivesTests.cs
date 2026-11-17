using Mediateca.Infrastructure.Libraries;
using Mediateca.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mediateca.UnitTests.Libraries;

/// <summary>El selector de carpetas con una carpeta de discos montados (en Docker, /discos).</summary>
public sealed class FolderBrowserDrivesTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"mediateca-unit-discos-{Guid.NewGuid():N}");
    private readonly string _media;
    private readonly string _drives;
    private readonly FolderBrowser _browser;

    public FolderBrowserDrivesTests()
    {
        _media = MediaPaths.Normalize(Path.Combine(_temp, "media"));
        _drives = MediaPaths.Normalize(Path.Combine(_temp, "discos"));
        Directory.CreateDirectory(_media);
        Directory.CreateDirectory(Path.Combine(_drives, "C", "Users"));
        Directory.CreateDirectory(Path.Combine(_drives, "D", "Peliculas", "Accion"));
        Directory.CreateDirectory(Path.Combine(_drives, "datos", "Musica"));

        // Igual que la configuración real: la carpeta de los discos también está entre las carpetas de medios.
        _browser = new FolderBrowser(Options.Create(new StorageOptions
        {
            MediaRoots = [_media, _drives],
            DrivesPath = _drives,
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    [Fact]
    public void Sin_ruta_lista_las_carpetas_de_medios_y_cada_disco_sin_mostrar_la_carpeta_de_los_discos()
    {
        var resultado = _browser.Browse(null);

        Assert.Null(resultado.Error);
        var carpetas = resultado.Listing!.Folders;
        Assert.Equal(new[] { _media, Path.Combine(_drives, "C"), Path.Combine(_drives, "D"), Path.Combine(_drives, "datos") },
            carpetas.Select(f => f.Path).ToArray());
        Assert.DoesNotContain(carpetas, f => MediaPaths.AreSame(f.Path, _drives));
    }

    [Fact]
    public void Nombra_los_discos_con_su_letra_y_las_demas_carpetas_con_su_nombre()
    {
        var carpetas = _browser.Browse(null).Listing!.Folders;

        Assert.Equal(new[] { "Disco C:", "Disco D:", "datos" }, carpetas.Skip(1).Select(f => f.Name).ToArray());
        Assert.Equal(new[] { "C:\\", "D:\\" }, carpetas.Skip(1).Take(2).Select(f => f.DisplayPath).ToArray());
        // Una carpeta de /discos que no es una letra se muestra con su ruta de siempre.
        Assert.Equal(Path.Combine(_drives, "datos"), carpetas[3].DisplayPath);
    }

    [Fact]
    public void Nombra_la_carpeta_de_medios_de_Docker()
    {
        var raiz = MediaPaths.Normalize(StorageOptions.DefaultMediaRoot);

        Assert.Equal("Carpeta de medios", FolderBrowser.NameOfRoot(raiz));
        // Cualquier otra carpeta de medios se nombra con su ruta.
        Assert.Equal(_media, FolderBrowser.NameOfRoot(_media));
    }

    [Fact]
    public void Dentro_de_un_disco_muestra_las_rutas_de_Windows()
    {
        var resultado = _browser.Browse(Path.Combine(_drives, "D"));

        Assert.Equal("D:\\", resultado.Listing!.DisplayPath);
        var peliculas = Assert.Single(resultado.Listing.Folders);
        Assert.Equal("Peliculas", peliculas.Name);
        Assert.Equal(Path.Combine(_drives, "D", "Peliculas"), peliculas.Path);
        Assert.Equal("D:\\Peliculas", peliculas.DisplayPath);

        var accion = _browser.Browse(peliculas.Path);
        Assert.Equal("D:\\Peliculas", accion.Listing!.DisplayPath);
        Assert.Equal("D:\\Peliculas\\Accion", Assert.Single(accion.Listing.Folders).DisplayPath);
    }

    [Fact]
    public void Arriba_de_un_disco_esta_la_lista_inicial_y_arriba_de_sus_carpetas_el_disco()
    {
        var disco = _browser.Browse(Path.Combine(_drives, "D"));
        var peliculas = _browser.Browse(Path.Combine(_drives, "D", "Peliculas"));

        Assert.Null(disco.Listing!.Parent);
        Assert.Equal(Path.Combine(_drives, "D"), peliculas.Listing!.Parent);
    }

    [Fact]
    public void La_carpeta_de_los_discos_no_se_explora_como_una_carpeta_mas()
    {
        var resultado = _browser.Browse(_drives);

        Assert.Null(resultado.Error);
        Assert.Null(resultado.Listing!.Path);
        Assert.Equal(_browser.Browse(null).Listing!.Folders.Count, resultado.Listing.Folders.Count);
    }

    [Fact]
    public void Si_la_carpeta_de_los_discos_no_existe_no_aparece_nada_ni_falla()
    {
        var navegador = new FolderBrowser(Options.Create(new StorageOptions
        {
            MediaRoots = [_media, Path.Combine(_temp, "no-existe")],
            DrivesPath = Path.Combine(_temp, "no-existe"),
        }));

        var resultado = navegador.Browse(null);

        Assert.Null(resultado.Error);
        Assert.Equal(new[] { _media }, resultado.Listing!.Folders.Select(f => f.Path).ToArray());
    }
}
