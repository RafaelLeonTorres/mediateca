using Mediateca.Infrastructure.Storage;
using Xunit;

namespace Mediateca.UnitTests.Libraries;

public class MediaPathsTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mediateca-ruta", "media"));

    [Fact]
    public void Una_carpeta_esta_dentro_de_si_misma_y_de_sus_subcarpetas()
    {
        Assert.True(MediaPaths.IsWithin(Root, Root));
        Assert.True(MediaPaths.IsWithin(Path.Combine(Root, "Peliculas"), Root));
        Assert.True(MediaPaths.IsWithin(Path.Combine(Root, "Peliculas", "Extras", "x.mp4"), Root));
        Assert.True(MediaPaths.IsWithin(Path.Combine(Root, "Peliculas"), Root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Un_prefijo_de_texto_no_cuenta_como_carpeta()
    {
        Assert.False(MediaPaths.IsWithin(Root + "2", Root));
        Assert.False(MediaPaths.IsWithin(Path.Combine(Root + "2", "Peliculas"), Root));
        Assert.False(MediaPaths.IsWithin(Path.GetDirectoryName(Root)!, Root));
    }

    [Fact]
    public void Resuelve_los_puntos_dobles_antes_de_comparar()
    {
        Assert.False(MediaPaths.IsWithin(Path.Combine(Root, "..", "otra"), Root));
        Assert.True(MediaPaths.IsWithin(Path.Combine(Root, "a", "..", "b"), Root));
    }

    [Fact]
    public void Normaliza_quitando_el_separador_final()
    {
        Assert.Equal(Root, MediaPaths.Normalize(Root + Path.DirectorySeparatorChar));
        Assert.True(MediaPaths.AreSame(Root, Root + Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("/discos/F/Peliculas/Accion", "F:\\Peliculas\\Accion")]
    [InlineData("/discos/f/Peliculas", "F:\\Peliculas")]
    [InlineData("/discos/F", "F:\\")]
    [InlineData("/discos/F/", "F:\\")]
    [InlineData("/discos/C/Users/Yo/Videos", "C:\\Users\\Yo\\Videos")]
    public void Muestra_las_carpetas_de_los_discos_como_en_Windows(string path, string esperada)
    {
        Assert.Equal(esperada, MediaPaths.ToDisplayPath(path, "/discos"));
    }

    [Theory]
    [InlineData("/discos/datos/Peliculas")]
    [InlineData("/discos/datos")]
    [InlineData("/discos")]
    [InlineData("/discos2/F/Peliculas")]
    [InlineData("/media/Peliculas")]
    [InlineData("/discos/FG/Peliculas")]
    [InlineData("/discos/1/Peliculas")]
    public void Deja_igual_las_demas_rutas(string path)
    {
        Assert.Equal(path, MediaPaths.ToDisplayPath(path, "/discos"));
    }

    [Fact]
    public void Muestra_los_discos_de_una_carpeta_de_discos_distinta()
    {
        var discos = Path.Combine(Path.GetTempPath(), "mediateca-discos");

        Assert.Equal("D:\\Series", MediaPaths.ToDisplayPath(Path.Combine(discos, "D", "Series"), discos));
        Assert.Equal("D:\\", MediaPaths.ToDisplayPath(Path.Combine(discos, "D"), discos));
    }

    [Fact]
    public void Distingue_mayusculas_solo_donde_el_sistema_las_distingue()
    {
        var upper = Root.ToUpperInvariant();
        var sensitive = !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

        Assert.Equal(!sensitive, MediaPaths.AreSame(Root, upper));
    }
}
