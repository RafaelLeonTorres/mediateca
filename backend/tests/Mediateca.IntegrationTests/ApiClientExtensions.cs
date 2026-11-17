namespace Mediateca.IntegrationTests;

internal static class ApiClientExtensions
{
    private const string XsrfCookie = "XSRF-TOKEN=";

    /// <summary>
    /// Hace un GET que entrega el token XSRF y lo pone en la cabecera X-XSRF-TOKEN, como hace
    /// Angular. La cookie antiforgery la guarda el propio cliente de pruebas.
    /// </summary>
    public static async Task UseXsrfTokenAsync(this HttpClient client, string url = "/api/auth/me")
    {
        using var response = await client.GetAsync(url);
        var cookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(XsrfCookie, StringComparison.Ordinal));
        var end = cookie.IndexOf(';');
        var token = end < 0 ? cookie[XsrfCookie.Length..] : cookie[XsrfCookie.Length..end];

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(token));
    }
}
