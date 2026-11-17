using Mediateca.Infrastructure.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mediateca.UnitTests.Scanning;

/// <summary>Los archivos vacíos de las pruebas (y los dañados) no deben romper el escaneo.</summary>
public sealed class TagLibAudioTagReaderTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"mediateca-unit-{Guid.NewGuid():N}");
    private readonly TagLibAudioTagReader _reader = new(NullLogger<TagLibAudioTagReader>.Instance);

    public TagLibAudioTagReaderTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("vacio.flac")]
    [InlineData("vacio.mp3")]
    [InlineData("vacio.mkv")]
    public void Un_archivo_vacio_no_lanza_excepciones(string nombre)
    {
        var ruta = Path.Combine(_temp, nombre);
        File.WriteAllBytes(ruta, Array.Empty<byte>());

        var error = Record.Exception(() =>
        {
            _reader.ReadTags(ruta);
            _reader.ReadDuration(ruta);
            _reader.ReadCover(ruta);
        });

        Assert.Null(error);
    }

    [Fact]
    public void Un_archivo_con_basura_devuelve_null()
    {
        var ruta = Path.Combine(_temp, "basura.flac");
        File.WriteAllText(ruta, "esto no es audio");

        Assert.Null(_reader.ReadTags(ruta));
        Assert.Null(_reader.ReadDuration(ruta));
        Assert.Null(_reader.ReadCover(ruta));
    }

    [Fact]
    public void Un_archivo_que_no_existe_devuelve_null()
    {
        var ruta = Path.Combine(_temp, "no-existe.mp3");

        Assert.Null(_reader.ReadTags(ruta));
        Assert.Null(_reader.ReadDuration(ruta));
        Assert.Null(_reader.ReadCover(ruta));
    }
}
