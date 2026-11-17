using System.Net;
using System.Net.Http.Json;
using Mediateca.Api.Auth;
using Mediateca.Api.Setup;

namespace Mediateca.IntegrationTests;

public sealed class AuthTests(MediatecaApiFactory factory) : IClassFixture<MediatecaApiFactory>, IAsyncLifetime
{
    private const string UserName = "admin";
    private const string Password = "una-clave-larga";

    /// <summary>Antes de cada prueba se asegura de que exista el administrador.</summary>
    public async Task InitializeAsync()
    {
        using var client = factory.CreateClient();
        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        if (status?.NeedsSetup == true)
        {
            await client.UseXsrfTokenAsync("/api/setup/status");
            var created = await client.PostAsJsonAsync("/api/setup", new SetupRequest("Administración", UserName, Password));
            created.EnsureSuccessStatusCode();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Login_con_clave_incorrecta_responde_401()
    {
        using var client = factory.CreateClient();
        await client.UseXsrfTokenAsync();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(UserName, "no-es-la-clave", false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_inicia_la_sesion_y_logout_la_cierra()
    {
        using var client = factory.CreateClient();
        await client.UseXsrfTokenAsync();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(UserName, Password, false));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(UserName, me?.UserName);

        // El token XSRF va ligado al usuario: después de iniciar sesión se pide uno nuevo.
        await client.UseXsrfTokenAsync();
        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_los_endpoints_protegidos_responden_401()
    {
        using var client = factory.CreateClient();
        await client.UseXsrfTokenAsync();

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
