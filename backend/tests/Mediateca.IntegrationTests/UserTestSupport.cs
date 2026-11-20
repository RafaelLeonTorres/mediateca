using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mediateca.Api.Auth;
using Mediateca.Api.Libraries;
using Mediateca.Api.Users;
using Mediateca.Core.Libraries;
using Mediateca.Infrastructure.Identity;

namespace Mediateca.IntegrationTests;

/// <summary>Utilidades de las pruebas de usuarios: altas por la API de administración, sesiones y lectura de errores.</summary>
internal static class UserTestSupport
{
    /// <summary>Nombre de usuario nuevo en cada llamada: las pruebas de una clase comparten la base de datos.</summary>
    public static string UniqueUserName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>Crea un usuario con la API de administración (con la contraseña de las pruebas, salvo que se indique otra).</summary>
    public static async Task<AdminUserResponse> CreateUserAsync(
        HttpClient admin,
        string userName,
        string role = Roles.Usuario,
        IEnumerable<Guid>? libraryIds = null,
        string? displayName = null,
        string password = TestUsers.Password)
    {
        var response = await admin.PostJsonAsync(
            "/api/admin/users",
            new CreateUserRequest(userName, displayName ?? userName, password, role, libraryIds?.ToList()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync<AdminUserResponse>())!;
    }

    /// <summary>Una biblioteca sin elementos (basta para los permisos). La carpeta queda dentro de la de medios de la prueba.</summary>
    public static Task<AdminLibraryResponse> CreateEmptyLibraryAsync(
        MediatecaApiFactory factory, HttpClient admin, LibraryType type = LibraryType.Movies) =>
        LibraryTestSupport.CreateLibraryAsync(
            admin, LibraryTestSupport.UniqueName("Biblioteca"), type, LibraryTestSupport.CreateFolder(factory, "Biblioteca"));

    /// <summary>Intenta iniciar sesión y devuelve la respuesta (204 si entró).</summary>
    public static async Task<HttpResponseMessage> TryLoginAsync(MediatecaApiFactory factory, string userName, string password)
    {
        using var client = factory.CreateClient();
        await client.UseXsrfTokenAsync();
        return await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password, false));
    }

    /// <summary>Código de una ruta que pide sesión: 200 mientras la sesión vale y 401 cuando dejó de valer.</summary>
    public static async Task<HttpStatusCode> SessionStatusAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/libraries");
        return response.StatusCode;
    }

    /// <summary>Los errores de validación (campo y mensajes) de una respuesta 400.</summary>
    public static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("errors").EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.EnumerateArray().Select(message => message.GetString()!).ToArray());
    }

    /// <summary>El título de una respuesta de error (ProblemDetails).</summary>
    public static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("title").GetString();
    }

    public static Task<HttpResponseMessage> UpdateAsync(
        HttpClient admin, Guid id, string displayName, string role, IEnumerable<Guid>? libraryIds = null) =>
        admin.PutJsonAsync($"/api/admin/users/{id}", new UpdateUserRequest(displayName, role, libraryIds?.ToList()));
}
