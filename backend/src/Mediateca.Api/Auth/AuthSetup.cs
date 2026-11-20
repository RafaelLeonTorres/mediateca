using System.Threading.RateLimiting;
using Mediateca.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Mediateca.Api.Auth;

/// <summary>Límite de intentos de inicio de sesión por IP (sección "Auth:LoginRateLimit").</summary>
public sealed class LoginRateLimitOptions
{
    public const string Section = "Auth:LoginRateLimit";

    /// <summary>Intentos permitidos por IP en cada ventana.</summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>Duración de la ventana.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Validez de la sesión (sección "Auth:Session").</summary>
public sealed class SessionValidationOptions
{
    public const string Section = "Auth:Session";

    /// <summary>
    /// Cada cuánto vuelve la cookie a comprobarse contra la base de datos (sello de seguridad). Una sesión
    /// cerrada desde el servidor (otro rol, otra contraseña, usuario borrado) deja de valer a más tardar en este
    /// tiempo. Identity trae 30 minutos; aquí son 30 segundos. Cero comprueba en cada solicitud.
    /// </summary>
    public TimeSpan ValidationInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public static class AuthSetup
{
    /// <summary>
    /// Política de límite para el inicio de sesión: por defecto 5 intentos por minuto por IP
    /// (<see cref="LoginRateLimitOptions"/>).
    /// </summary>
    public const string LoginRateLimit = "login";

    /// <summary>Política de autorización para la administración.</summary>
    public const string AdminPolicy = "Admin";

    /// <summary>
    /// Sesión con la cookie de Identity (las etiquetas &lt;video&gt; y &lt;audio&gt; la mandan solas),
    /// protección XSRF, autorización por defecto y límite de intentos de inicio de sesión.
    /// </summary>
    public static IServiceCollection AddMediatecaAuth(this IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "mediateca.sesion";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Secure cuando la app se sirve por HTTPS; en la red local se usa HTTP.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            // Es una API: en lugar de redirigir a una página de login, responde 401 o 403.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // El sello de seguridad de Identity cambia con la contraseña y con el rol (UpdateSecurityStampAsync). La
        // cookie lo comprueba cada ValidationInterval: con un sello distinto, o con el usuario borrado, la sesión
        // se rechaza y se vuelve a pedir el inicio de sesión.
        services.AddOptions<SessionValidationOptions>().BindConfiguration(SessionValidationOptions.Section);
        services.AddOptions<SecurityStampValidatorOptions>()
            .Configure<IOptions<SessionValidationOptions>>((options, session) =>
                options.ValidationInterval = session.Value.ValidationInterval);

        // Todo endpoint pide sesión salvo los marcados con AllowAnonymous.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy.RequireRole(Roles.Admin));

        services.AddAntiforgery(options =>
        {
            options.HeaderName = Xsrf.HeaderName;
            options.Cookie.Name = "mediateca.antiforgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddOptions<LoginRateLimitOptions>().BindConfiguration(LoginRateLimitOptions.Section);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<LoginRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limit.PermitLimit, Window = limit.Window });
            });
        });

        return services;
    }
}
