using System.Net;
using Mediateca.Api.Libraries;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Scanning;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>El horario del escaneo automático (GET y PUT /api/admin/scan-schedule) y quién puede cambiarlo.</summary>
public sealed class ScanScheduleTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    private const string RegularUser = "usuario";
    private const string Url = "/api/admin/scan-schedule";

    public async Task InitializeAsync()
    {
        await TestUsers.EnsureAdminAsync(factory);
        await TestUsers.EnsureUserAsync(factory, RegularUser);

        // Las pruebas de la clase comparten la base de datos: cada una parte de cero, sin horario guardado.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        await db.ScanSchedules.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sin_horario_guardado_devuelve_el_de_la_configuracion_con_sus_proximas_ejecuciones()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);

        var schedule = (await admin.GetJsonAsync<ScanScheduleDescription>(Url))!;

        Assert.True(schedule.Enabled);
        Assert.Equal("0 0 3 * * ?", schedule.CronExpression);
        Assert.Equal(TimeZoneInfo.Local.Id, schedule.TimeZone);
        Assert.Equal(3, schedule.NextRuns.Count);
        Assert.All(schedule.NextRuns, run => Assert.True(run > DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.True(schedule.NextRuns[0] < schedule.NextRuns[1] && schedule.NextRuns[1] < schedule.NextRuns[2]);
    }

    [Fact]
    public async Task Un_horario_valido_se_guarda_y_devuelve_las_proximas_ejecuciones()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);
        var before = (await admin.GetJsonAsync<ScanScheduleDescription>(Url))!;

        var response = await admin.PutJsonAsync(Url, new ScanScheduleRequest(true, "0 0 */6 * * ?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.ReadJsonAsync<ScanScheduleDescription>())!;
        Assert.True(saved.Enabled);
        Assert.Equal("0 0 */6 * * ?", saved.CronExpression);
        Assert.Equal(3, saved.NextRuns.Count);
        Assert.Equal(TimeSpan.FromHours(6), saved.NextRuns[1] - saved.NextRuns[0]);
        Assert.True(saved.UpdatedAt >= before.UpdatedAt);

        var again = (await admin.GetJsonAsync<ScanScheduleDescription>(Url))!;
        Assert.Equal("0 0 */6 * * ?", again.CronExpression);
    }

    [Fact]
    public async Task Una_expresion_que_no_es_cron_responde_400_y_no_cambia_nada()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);

        var response = await admin.PutJsonAsync(Url, new ScanScheduleRequest(true, "esto no es un cron"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.ReadJsonAsync<HttpValidationProblemDetails>())!;
        Assert.Contains("no es válida", Assert.Single(problem.Errors["cronExpression"]));
        Assert.Equal("0 0 3 * * ?", (await admin.GetJsonAsync<ScanScheduleDescription>(Url))!.CronExpression);
    }

    [Fact]
    public async Task Un_horario_cada_minuto_responde_400_por_el_intervalo_minimo()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);

        var response = await admin.PutJsonAsync(Url, new ScanScheduleRequest(true, "0 * * * * ?"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.ReadJsonAsync<HttpValidationProblemDetails>())!;
        Assert.Equal("Elige un horario con al menos 5 minutos entre escaneos.", Assert.Single(problem.Errors["cronExpression"]));
    }

    [Fact]
    public async Task Sin_decir_si_esta_activo_responde_400()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);

        var response = await admin.PutJsonAsync(Url, new ScanScheduleRequest(null, "0 0 3 * * ?"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.ReadJsonAsync<HttpValidationProblemDetails>())!;
        Assert.Contains("enabled", problem.Errors.Keys);
    }

    [Fact]
    public async Task Desactivar_el_escaneo_automatico_conserva_el_horario_y_quita_las_proximas_ejecuciones()
    {
        using var admin = await TestUsers.LoginAsync(factory, TestUsers.AdminUserName);

        var response = await admin.PutJsonAsync(Url, new ScanScheduleRequest(false, "0 0 3 ? * SUN"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.ReadJsonAsync<ScanScheduleDescription>())!;
        Assert.False(saved.Enabled);
        Assert.Equal("0 0 3 ? * SUN", saved.CronExpression);
        Assert.Empty(saved.NextRuns);

        var again = (await admin.GetJsonAsync<ScanScheduleDescription>(Url))!;
        Assert.False(again.Enabled);
        Assert.Equal("0 0 3 ? * SUN", again.CronExpression);
        Assert.Empty(again.NextRuns);
    }

    [Theory]
    [InlineData("GET", "/api/admin/scan-schedule")]
    [InlineData("PUT", "/api/admin/scan-schedule")]
    [InlineData("POST", "/api/admin/libraries/scan-all")]
    public async Task Sin_sesion_responde_401(string method, string url)
    {
        using var client = factory.CreateClient();
        await client.UseXsrfTokenAsync();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/admin/scan-schedule")]
    [InlineData("PUT", "/api/admin/scan-schedule")]
    [InlineData("POST", "/api/admin/libraries/scan-all")]
    public async Task Un_usuario_sin_permisos_de_administrador_recibe_403(string method, string url)
    {
        using var client = await TestUsers.LoginAsync(factory, RegularUser);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
