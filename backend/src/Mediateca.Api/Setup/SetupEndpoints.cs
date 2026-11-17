using Mediateca.Api.Auth;
using Mediateca.Infrastructure.Identity;
using Mediateca.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mediateca.Api.Setup;

public sealed record SetupStatusResponse(bool NeedsSetup);

public sealed record SetupRequest(string DisplayName, string UserName, string Password);

/// <summary>
/// Primer arranque: mientras no haya usuarios, Angular muestra el asistente que crea al
/// administrador. Después, POST /api/setup responde 404.
/// </summary>
public static class SetupEndpoints
{
    // Llave del candado de PostgreSQL que serializa las solicitudes de setup.
    private const long SetupLockKey = 4242;

    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup").WithTags("Primer arranque").AllowAnonymous();

        group.MapGet("/status", GetStatusAsync)
            .WithSummary("Indica si falta crear al administrador. También entrega el token XSRF.");

        group.MapPost("", CreateAdminAsync)
            .WithSummary("Crea al primer administrador e inicia su sesión. Solo funciona mientras no haya usuarios.");

        return app;
    }

    private static async Task<Ok<SetupStatusResponse>> GetStatusAsync(
        HttpContext http, UserManager<AppUser> users, CancellationToken cancellationToken)
    {
        Xsrf.IssueToken(http);
        return TypedResults.Ok(new SetupStatusResponse(!await users.Users.AnyAsync(cancellationToken)));
    }

    private static async Task<Results<Created<UserResponse>, NotFound, ValidationProblem>> CreateAdminAsync(
        SetupRequest request,
        MediatecaDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Si llegan dos solicitudes a la vez, el candado hace esperar a la segunda, que después ve
        // que ya hay un usuario. Así nunca se crean dos administradores.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SetupLockKey})", cancellationToken);
        if (await users.Users.AnyAsync(cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var user = new AppUser
        {
            UserName = request.UserName.Trim(),
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return TypedResults.ValidationProblem(ToErrors(created));
        }

        var addedToRole = await users.AddToRoleAsync(user, Roles.Admin);
        if (!addedToRole.Succeeded)
        {
            return TypedResults.ValidationProblem(ToErrors(addedToRole));
        }

        await transaction.CommitAsync(cancellationToken);

        await signIn.SignInAsync(user, isPersistent: true);
        return TypedResults.Created("/api/auth/me", await AuthEndpoints.ToResponseAsync(user, users));
    }

    private static Dictionary<string, string[]> Validate(SetupRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors["displayName"] = ["Escribe tu nombre."];
        }
        else if (request.DisplayName.Trim().Length > 100)
        {
            errors["displayName"] = ["El nombre puede tener hasta 100 caracteres."];
        }

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            errors["userName"] = ["Escribe un nombre de usuario."];
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            errors["password"] = ["Escribe una contraseña."];
        }

        return errors;
    }

    // Los errores de Identity se agrupan por campo para que Angular los muestre junto a cada uno.
    private static Dictionary<string, string[]> ToErrors(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "userName")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
