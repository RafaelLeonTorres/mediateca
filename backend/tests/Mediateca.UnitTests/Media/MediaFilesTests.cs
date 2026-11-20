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
    [InlineData("Tears of Steel (2012).MOV")]
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
    [InlineData(LibraryType.Movies, "pelicula.mov", true)]
    [InlineData(LibraryType.Movies, "pista.flac", false)]
    public void Filtra_por_tipo_de_biblioteca(LibraryType tipo, string archivo, bool esperado) =>
        Assert.Equal(esperado, MediaFiles.BelongsTo(tipo, archivo));

    [Theory]
    [InlineData("pelicula.mp4", "video/mp4")]
    [InlineData("pelicula.M4V", "video/mp4")]
    [InlineData("pelicula.mov", "video/mp4")]
    [InlineData("clip.webm", "video/webm")]
    [InlineData("serie.mkv", "video/x-matroska")]
    [InlineData("pista.mp3", "audio/mpeg")]
    [InlineData("pista.m4a", "audio/mp4")]
    [InlineData("pista.aac", "audio/aac")]
    [InlineData("pista.FLAC", "audio/flac")]
    [InlineData("pista.ogg", "audio/ogg")]
    [InlineData("pista.opus", "audio/ogg")]
    [InlineData("pista.wav", "audio/wav")]
    public void Da_el_tipo_MIME_de_cada_extension(string archivo, string esperado) =>
        Assert.Equal(esperado, MediaFiles.ContentTypeOf(archivo));

    [Theory]
    [InlineData("portada.jpg")]
    [InlineData("subtitulos.srt")]
    [InlineData("sin-extension")]
    public void No_da_tipo_MIME_a_lo_que_no_reconoce(string archivo) =>
        Assert.Null(MediaFiles.ContentTypeOf(archivo));
}
