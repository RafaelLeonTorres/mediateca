using Mediateca.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Mediateca.Api.Auth;

public sealed record LoginRequest(string UserName, string Password, bool RememberMe);

public sealed record UserResponse(Guid Id, string UserName, string DisplayName, bool IsAdmin);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Sesión");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.LoginRateLimit)
            .WithSummary("Inicia sesión con usuario y contraseña. La sesión queda en una cookie.");

        group.MapPost("/logout", LogoutAsync)
            .WithSummary("Cierra la sesión.");

        group.MapGet("/me", MeAsync)
            .AllowAnonymous()
            .WithSummary("Usuario de la sesión actual (401 si no hay sesión). También entrega el token XSRF.");

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LoginAsync(
        LoginRequest request, SignInManager<AppUser> signIn)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
        {
            return InvalidCredentials();
        }

        var result = await signIn.PasswordSignInAsync(
            request.UserName.Trim(), request.Password, request.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return TypedResults.NoContent();
        }

        if (result.IsLockedOut)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Demasiados intentos fallidos. La cuenta queda bloqueada unos minutos.");
        }

        return InvalidCredentials();
    }

    private static async Task<NoContent> LogoutAsync(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult>> MeAsync(
        HttpContext http, UserManager<AppUser> users)
    {
        Xsrf.IssueToken(http);

        if (http.User.Identity?.IsAuthenticated != true)
        {
            return TypedResults.Unauthorized();
        }

        var user = await users.GetUserAsync(http.User);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(await ToResponseAsync(user, users));
    }

    internal static async Task<UserResponse> ToResponseAsync(AppUser user, UserManager<AppUser> users) =>
        new(user.Id, user.UserName!, user.DisplayName, await users.IsInRoleAsync(user, Roles.Admin));

    // El mismo mensaje para usuario inexistente y contraseña equivocada: no revela qué usuarios existen.
    private static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Usuario o contraseña incorrectos.");
}
