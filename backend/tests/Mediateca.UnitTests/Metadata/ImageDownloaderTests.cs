using Mediateca.Infrastructure.Metadata;

namespace Mediateca.UnitTests.Metadata;

public class ImageDownloaderTests
{
    [Theory]
    [InlineData("https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx21.jpg")]
    [InlineData("https://anilist.co/img/x.png")]
    [InlineData("https://m.media-amazon.com/images/M/abc.jpg")]
    [InlineData("HTTPS://M.MEDIA-AMAZON.COM/images/M/abc.jpg")]
    public void Acepta_las_imagenes_https_de_los_servidores_conocidos(string url)
    {
        Assert.True(ImageDownloader.IsAllowed(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es una direccion")]
    [InlineData("http://s4.anilist.co/x.jpg")]
    [InlineData("https://evil.test/x.jpg")]
    [InlineData("https://anilist.co.evil.test/x.jpg")]
    [InlineData("https://notanilist.co/x.jpg")]
    [InlineData("https://s4.anilist.co:8443/x.jpg")]
    [InlineData("https://usuario:clave@s4.anilist.co/x.jpg")]
    [InlineData("https://127.0.0.1/x.jpg")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://s4.anilist.co/x.jpg")]
    public void Rechaza_lo_que_no_es_de_un_servidor_conocido_por_https(string? url)
    {
        Assert.False(ImageDownloader.IsAllowed(url));
    }

    [Fact]
    public void Reconoce_el_formato_por_los_primeros_bytes()
    {
        Assert.Equal(".jpg", ImageDownloader.Sniff([0xFF, 0xD8, 0xFF, 0xE0, 0x00]));
        Assert.Equal(".png", ImageDownloader.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal(".webp", ImageDownloader.Sniff("RIFF\0\0\0\0WEBPVP8 "u8));
    }

    [Fact]
    public void Lo_que_no_es_una_imagen_no_tiene_formato()
    {
        Assert.Null(ImageDownloader.Sniff("<html>"u8));
        Assert.Null(ImageDownloader.Sniff("GIF89a"u8));
        Assert.Null(ImageDownloader.Sniff([]));
        Assert.Null(ImageDownloader.Sniff([0xFF, 0xD8]));
    }
}
