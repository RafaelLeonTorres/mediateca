using System.Threading.RateLimiting;
using Mediateca.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace Mediateca.Api.Auth;

public static class AuthSetup
{
    /// <summary>Política de límite para el inicio de sesión: 5 intentos por minuto por IP.</summary>
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

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }
}
