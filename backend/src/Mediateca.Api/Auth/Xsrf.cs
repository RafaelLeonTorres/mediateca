using Microsoft.AspNetCore.Antiforgery;

namespace Mediateca.Api.Auth;

/// <summary>
/// Protección XSRF para una SPA. La API entrega el token en la cookie XSRF-TOKEN, que JavaScript
/// sí puede leer, y Angular lo devuelve en la cabecera X-XSRF-TOKEN en cada POST, PUT o DELETE.
/// Un sitio ajeno no puede leer esa cookie, así que no puede armar una petición válida.
/// </summary>
public static class Xsrf
{
    public const string CookieName = "XSRF-TOKEN";
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>
    /// Genera el token para el usuario actual. El token va ligado al usuario, por eso Angular vuelve
    /// a pedir /api/auth/me después de iniciar o cerrar sesión.
    /// </summary>
    public static void IssueToken(HttpContext http)
    {
        var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(http);
        http.Response.Cookies.Append(CookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            Path = "/",
        });
    }

    /// <summary>Rechaza con 400 los POST, PUT, PATCH y DELETE a /api que no traigan un token válido.</summary>
    public static IApplicationBuilder UseXsrfValidation(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (RequiresValidation(context.Request) &&
                !await context.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(context))
            {
                await TypedResults.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Falta el token XSRF o no es válido.",
                        detail: "Recarga la página e inténtalo de nuevo.")
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });

    private static bool RequiresValidation(HttpRequest request) =>
        request.Path.StartsWithSegments("/api") &&
        !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
          HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method));
}
