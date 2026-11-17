using Mediateca.Infrastructure.Scanning;
using Xunit;

namespace Mediateca.UnitTests.Scanning;

public class ScanScheduleRulesTests
{
    private static readonly TimeSpan Minimum = TimeSpan.FromMinutes(5);

    // Martes 17 de noviembre de 2026, 10:30 UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);

    private static string? Validate(string? cron) =>
        ScanScheduleRules.Validate(cron, TimeZoneInfo.Utc, Minimum, Now);

    [Theory]
    [InlineData("0 0 * * * ?")]
    [InlineData("0 0 */6 * * ?")]
    [InlineData("0 0 */12 * * ?")]
    [InlineData("0 0 3 * * ?")]
    [InlineData("0 0 3 ? * SUN")]
    [InlineData("0 0/5 * * * ?")]
    [InlineData("  0 0 3 * * ?  ")]
    // Quartz 4 ya acepta * en el día del mes y en el día de la semana a la vez (la 3.x pedía ?).
    [InlineData("0 0 3 * * *")]
    public void Acepta_los_horarios_con_al_menos_cinco_minutos_entre_escaneos(string cron)
    {
        Assert.Null(Validate(cron));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Pide_la_expresion_cuando_esta_vacia(string? cron)
    {
        Assert.Equal("Escribe la expresión del horario.", Validate(cron));
    }

    [Theory]
    [InlineData("esto no es un cron")]
    [InlineData("0 0 25 * * ?")]
    [InlineData("0 61 3 * * ?")]
    public void Rechaza_las_expresiones_que_no_son_validas(string cron)
    {
        Assert.Contains("no es válida", Validate(cron));
    }

    [Fact]
    public void Rechaza_las_expresiones_demasiado_largas()
    {
        var cron = "0 0 3 * * ?" + new string(' ', ScanScheduleRules.MaxCronLength);

        Assert.Contains("hasta 120 caracteres", Validate(cron + "x"));
    }

    [Theory]
    [InlineData("0 * * * * ?")]
    [InlineData("0 0/4 * * * ?")]
    [InlineData("0 0,1 * * * ?")]
    public void Rechaza_los_horarios_con_menos_de_cinco_minutos_entre_escaneos(string cron)
    {
        Assert.Equal("Elige un horario con al menos 5 minutos entre escaneos.", Validate(cron));
    }

    [Fact]
    public void Revisa_varios_intervalos_y_no_solo_los_dos_primeros()
    {
        // Entre :10 y :59 hay 49 minutos, pero entre :59 y :00 de la hora siguiente solo hay 1.
        Assert.Equal(
            "Elige un horario con al menos 5 minutos entre escaneos.",
            Validate("0 0,10,59 * * * ?"));
    }

    [Fact]
    public void El_minimo_se_toma_de_las_opciones()
    {
        var result = ScanScheduleRules.Validate("0 0/10 * * * ?", TimeZoneInfo.Utc, TimeSpan.FromMinutes(15), Now);

        Assert.Equal("Elige un horario con al menos 15 minutos entre escaneos.", result);
    }

    [Fact]
    public void Calcula_las_proximas_ejecuciones_de_un_horario_diario()
    {
        var runs = ScanScheduleRules.NextRuns("0 0 3 * * ?", TimeZoneInfo.Utc, Now, 3);

        Assert.Equal(
            new[]
            {
                new DateTime(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 19, 3, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc),
            },
            runs.Select(run => run.UtcDateTime).ToArray());
    }

    [Fact]
    public void Calcula_las_proximas_ejecuciones_de_un_horario_por_hora()
    {
        var runs = ScanScheduleRules.NextRuns("0 0 * * * ?", TimeZoneInfo.Utc, Now, 3);

        Assert.Equal(
            new[]
            {
                new DateTime(2026, 11, 17, 11, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc),
            },
            runs.Select(run => run.UtcDateTime).ToArray());
    }

    [Fact]
    public void El_horario_semanal_cae_en_domingo()
    {
        var runs = ScanScheduleRules.NextRuns("0 0 3 ? * SUN", TimeZoneInfo.Utc, Now, 2);

        Assert.Equal(
            new[]
            {
                new DateTime(2026, 11, 22, 3, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 29, 3, 0, 0, DateTimeKind.Utc),
            },
            runs.Select(run => run.UtcDateTime).ToArray());
    }

    [Fact]
    public void Interpreta_el_horario_en_la_zona_horaria_indicada()
    {
        // Seis horas detrás de UTC (sin horario de verano): las 3:00 locales son las 9:00 UTC.
        var zone = TimeZoneInfo.CreateCustomTimeZone("prueba", TimeSpan.FromHours(-6), "prueba", "prueba");

        var runs = ScanScheduleRules.NextRuns("0 0 3 * * ?", zone, Now, 1);

        Assert.Equal(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc), Assert.Single(runs).UtcDateTime);
    }

    [Theory]
    [InlineData("esto no es un cron")]
    [InlineData("")]
    public void Sin_una_expresion_valida_no_hay_ejecuciones(string cron)
    {
        Assert.Empty(ScanScheduleRules.NextRuns(cron, TimeZoneInfo.Utc, Now, 3));
    }
}
