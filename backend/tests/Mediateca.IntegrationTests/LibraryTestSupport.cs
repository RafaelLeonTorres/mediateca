using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mediateca.Api.Auth;
using Mediateca.Api.Libraries;
using Mediateca.Api.Setup;
using Mediateca.Core.Libraries;
using Mediateca.Core.Scanning;
using Mediateca.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Mediateca.IntegrationTests;

/// <summary>Opciones de JSON de las pruebas: igual que la API, con los enums como texto.</summary>
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

internal static class JsonClientExtensions
{
    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T value) =>
        client.PostAsJsonAsync(url, value, TestJson.Options);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T value) =>
        client.PutAsJsonAsync(url, value, TestJson.Options);

    public static Task<T?> GetJsonAsync<T>(this HttpClient client, string url) =>
        client.GetFromJsonAsync<T>(url, TestJson.Options);

    public static Task<T?> ReadJsonAsync<T>(this HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<T>(TestJson.Options);
}

/// <summary>Usuarios y sesiones de las pruebas.</summary>
internal static class TestUsers
{
    public const string AdminUserName = "admin";
    public const string Password = "una-clave-larga";

    /// <summary>Crea al administrador con el asistente de primer arranque, si todavía no existe.</summary>
    public static async Task EnsureAdminAsync(MediatecaApiFactory factory)
    {
        using var client = factory.CreateClient();
        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        if (status?.NeedsSetup == true)
        {
            await client.UseXsrfTokenAsync("/api/setup/status");
            var created = await client.PostAsJsonAsync("/api/setup", new SetupRequest("Administración", AdminUserName, Password));
            created.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Crea un usuario sin permisos de administrador (rol Usuario), si todavía no existe.</summary>
    public static async Task EnsureUserAsync(MediatecaApiFactory factory, string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByNameAsync(userName) is not null)
        {
            return;
        }

        var user = new AppUser { UserName = userName, DisplayName = userName, CreatedAt = DateTimeOffset.UtcNow };

        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Select(e => e.Description)));
        var inRole = await users.AddToRoleAsync(user, Roles.Usuario);
        Assert.True(inRole.Succeeded, string.Join(" ", inRole.Errors.Select(e => e.Description)));
    }

    public static async Task<Guid> GetUserIdAsync(MediatecaApiFactory factory, string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByNameAsync(userName);
        return user!.Id;
    }

    /// <summary>Cliente con la sesión iniciada y el token XSRF de ese usuario.</summary>
    public static async Task<HttpClient> LoginAsync(MediatecaApiFactory factory, string userName)
    {
        var client = factory.CreateClient();
        await client.UseXsrfTokenAsync();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, Password, false));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        // El token XSRF va ligado al usuario: después de iniciar sesión se pide uno nuevo.
        await client.UseXsrfTokenAsync();
        return client;
    }
}

/// <summary>Árboles de carpetas con archivos vacíos para escanear, y utilidades para la API de bibliotecas.</summary>
internal static class LibraryTestSupport
{
    /// <summary>
    /// Crea una carpeta nueva dentro de la carpeta de medios con los archivos indicados (vacíos) y
    /// devuelve su ruta. Cada llamada usa una carpeta distinta: las pruebas comparten la base de datos.
    /// </summary>
    public static string CreateFolder(MediatecaApiFactory factory, string name, params string[] relativeFiles)
    {
        var folder = Path.Combine(factory.MediaPath, Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(folder);
        foreach (var relative in relativeFiles)
        {
            WriteFile(folder, relative, []);
        }

        return folder;
    }

    public static void WriteFile(string folder, string relative, byte[] content)
    {
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    public static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    public static async Task<AdminLibraryResponse> CreateLibraryAsync(
        HttpClient admin, string name, LibraryType type, params string[] paths)
    {
        var response = await admin.PostJsonAsync("/api/admin/libraries", new LibraryRequest(name, type, [.. paths]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync<AdminLibraryResponse>())!;
    }

    /// <summary>Pide un escaneo y espera a que termine (con tiempo límite).</summary>
    public static async Task<ScanJobResponse> ScanAsync(HttpClient admin, Guid libraryId)
    {
        var accepted = await admin.PostAsync($"/api/admin/libraries/{libraryId}/scan", null);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var job = (await accepted.ReadJsonAsync<ScanJobResponse>())!;

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var current = (await admin.GetJsonAsync<ScanJobResponse>($"/api/admin/scans/{job.Id}"))!;
            if (current.Status is ScanStatus.Completed or ScanStatus.Failed)
            {
                return current;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"El escaneo {job.Id} no terminó en 60 segundos.");
    }
}
