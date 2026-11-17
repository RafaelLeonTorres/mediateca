using Mediateca.Core.Users;
using Xunit;

namespace Mediateca.UnitTests.Users;

public class PlaybackProgressTests
{
    [Theory]
    [InlineData(0d, 100d, false)]
    [InlineData(50d, 100d, false)]
    [InlineData(89.9, 100d, false)]
    [InlineData(90d, 100d, false)]
    [InlineData(90.1, 100d, true)]
    [InlineData(95d, 100d, true)]
    [InlineData(100d, 100d, true)]
    [InlineData(150d, 100d, true)]
    public void Pasado_el_90_por_ciento_de_la_duracion_esta_visto(double posicion, double duracion, bool esperado) =>
        Assert.Equal(esperado, PlaybackProgress.IsFinished(posicion, duracion));

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-5d)]
    public void Sin_una_duracion_conocida_nunca_esta_visto(double? duracion) =>
        Assert.False(PlaybackProgress.IsFinished(10_000d, duracion));
}
