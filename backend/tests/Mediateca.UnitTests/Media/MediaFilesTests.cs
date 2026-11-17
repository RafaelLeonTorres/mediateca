using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Xunit;

namespace Mediateca.UnitTests.Media;

public class MediaFilesTests
{
    [Theory]
    [InlineData("Big Buck Bunny (2008).mp4")]
    [InlineData("Mi Serie S01E02.MKV")]
    [InlineData("clip.webm")]
    public void Reconoce_video(string archivo) => Assert.True(MediaFiles.IsVideo(archivo));

    [Theory]
    [InlineData("01 - Canción.flac")]
    [InlineData("pista.MP3")]
    [InlineData("tema.opus")]
    public void Reconoce_audio(string archivo) => Assert.True(MediaFiles.IsAudio(archivo));

    [Theory]
    [InlineData("portada.jpg")]
    [InlineData("subtitulos.srt")]
    [InlineData("sin-extension")]
    public void Ignora_otros_archivos(string archivo)
    {
        Assert.False(MediaFiles.IsVideo(archivo));
        Assert.False(MediaFiles.IsAudio(archivo));
    }

    [Theory]
    [InlineData(LibraryType.Music, "pista.mp3", true)]
    [InlineData(LibraryType.Music, "pelicula.mp4", false)]
    [InlineData(LibraryType.Anime, "episodio.mkv", true)]
    [InlineData(LibraryType.Movies, "pista.flac", false)]
    public void Filtra_por_tipo_de_biblioteca(LibraryType tipo, string archivo, bool esperado) =>
        Assert.Equal(esperado, MediaFiles.BelongsTo(tipo, archivo));
}
