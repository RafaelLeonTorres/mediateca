using Mediateca.Infrastructure.Libraries;
using Mediateca.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mediateca.UnitTests.Libraries;

/// <summary>Valida carpetas reales dentro de una carpeta temporal: /media, /media2 y subcarpetas.</summary>
public sealed class LibraryPathValidatorTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"mediateca-unit-{Guid.NewGuid():N}");
    private readonly string _media;
    private readonly string _otherMedia;
    private readonly LibraryPathValidator _validator;

    public LibraryPathValidatorTests()
    {
        _media = Path.Combine(_temp, "media");
        _otherMedia = Path.Combine(_temp, "media2");
        Directory.CreateDirectory(Path.Combine(_media, "Peliculas", "Extras"));
        Directory.CreateDirectory(Path.Combine(_media, "Series"));
        Directory.CreateDirectory(Path.Combine(_otherMedia, "Peliculas"));
        File.WriteAllText(Path.Combine(_media, "archivo.txt"), "");

        var options = Options.Create(new StorageOptions { MediaRoots = [MediaPaths.Normalize(_media)] });
        _validator = new LibraryPathValidator(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    [Fact]
    public void Acepta_carpetas_dentro_de_la_carpeta_de_medios()
    {
        var peliculas = Path.Combine(_media, "Peliculas");
        var series = Path.Combine(_media, "Series") + Path.DirectorySeparatorChar;

        var resultado = _validator.Validate([peliculas, series]);

        Assert.True(resultado.IsValid, string.Join(" ", resultado.Errors));
        // Las rutas salen normalizadas, sin separador final.
        Assert.Equal(new[] { peliculas, Path.Combine(_media, "Series") }, resultado.Paths.ToArray());
    }

    [Fact]
    public void Acepta_la_propia_carpeta_de_medios()
    {
        Assert.True(_validator.Validate([_media]).IsValid);
    }

    [Fact]
    public void Rechaza_una_carpeta_hermana_con_el_mismo_prefijo()
    {
        // "media2" empieza con "media", pero no está dentro de ella.
        var resultado = _validator.Validate([Path.Combine(_otherMedia, "Peliculas")]);

        Assert.False(resultado.IsValid);
        Assert.Contains("fuera de las carpetas de medios", resultado.Errors[0]);
    }

    [Fact]
    public void Rechaza_las_rutas_que_salen_de_la_carpeta_con_puntos_dobles()
    {
        var resultado = _validator.Validate([Path.Combine(_media, "Series", "..", "..", "media2", "Peliculas")]);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Rechaza_carpetas_que_no_existen_o_son_archivos()
    {
        var inexistente = _validator.Validate([Path.Combine(_media, "NoExiste")]);
        var archivo = _validator.Validate([Path.Combine(_media, "archivo.txt")]);

        Assert.Contains("no existe", inexistente.Errors[0]);
        Assert.False(archivo.IsValid);
    }

    [Fact]
    public void Rechaza_rutas_relativas()
    {
        var resultado = _validator.Validate(["Peliculas"]);

        Assert.False(resultado.IsValid);
        Assert.Contains("ruta absoluta", resultado.Errors[0]);
    }

    [Fact]
    public void Rechaza_una_lista_vacia_o_con_carpetas_vacias()
    {
        Assert.False(_validator.Validate([]).IsValid);
        Assert.False(_validator.Validate(null).IsValid);
        Assert.False(_validator.Validate([" "]).IsValid);
    }

    [Fact]
    public void Rechaza_carpetas_repetidas()
    {
        var peliculas = Path.Combine(_media, "Peliculas");

        var resultado = _validator.Validate([peliculas, peliculas + Path.DirectorySeparatorChar]);

        Assert.False(resultado.IsValid);
        Assert.Contains("repetida", resultado.Errors[0]);
    }

    [Fact]
    public void Rechaza_carpetas_anidadas()
    {
        var resultado = _validator.Validate([
            Path.Combine(_media, "Peliculas"),
            Path.Combine(_media, "Peliculas", "Extras"),
        ]);

        Assert.False(resultado.IsValid);
        Assert.Contains("se contienen", resultado.Errors[0]);
    }
}
