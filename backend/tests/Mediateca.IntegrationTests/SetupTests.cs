using System.Net;
using System.Net.Http.Json;
using Mediateca.Api.Auth;
using Mediateca.Api.Setup;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

public sealed class SetupTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>
{
    [Fact]
    public async Task Crea_al_administrador_inicia_su_sesion_y_solo_funciona_una_vez()
    {
        using var client = factory.CreateClient();
        await client.UseXsrfTokenAsync("/api/setup/status");

        var created = await client.PostAsJsonAsync("/api/setup", new SetupRequest("Rafael", "admin", "una-clave-larga"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // La sesión queda iniciada y el usuario es administrador.
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal("admin", me?.UserName);
        Assert.True(me?.IsAdmin);

        // Ya no se pide el primer arranque, y un segundo intento responde 404.
        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        Assert.False(status?.NeedsSetup);

        await client.UseXsrfTokenAsync();
        var again = await client.PostAsJsonAsync("/api/setup", new SetupRequest("Otra persona", "otro", "otra-clave-larga"));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Sin_token_XSRF_responde_400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/setup", new SetupRequest("Rafael", "admin", "una-clave-larga"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

/// <summary>Dos solicitudes de setup al mismo tiempo: solo una crea al administrador.</summary>
public sealed class SetupConcurrencyTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>
{
    [Fact]
    public async Task Dos_solicitudes_a_la_vez_crean_un_solo_administrador()
    {
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        await first.UseXsrfTokenAsync("/api/setup/status");
        await second.UseXsrfTokenAsync("/api/setup/status");

        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/setup", new SetupRequest("Uno", "uno", "una-clave-larga")),
            second.PostAsJsonAsync("/api/setup", new SetupRequest("Dos", "dos", "otra-clave-larga")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NotFound);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediatecaDbContext>();
        Assert.Equal(1, await db.Users.CountAsync());
    }
}
