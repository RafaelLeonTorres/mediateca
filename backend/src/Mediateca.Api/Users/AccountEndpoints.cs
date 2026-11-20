using Mediateca.Api.Auth;
using Mediateca.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Mediateca.Api.Users;

/// <summary>
/// Mi cuenta: cualquier usuario con sesión ve y cambia su nombre visible y su contraseña. Al cambiar la
/// contraseña, la sesión actual sigue valiendo y las demás se cierran.
/// </summary>
public static class AccountEndpoints
{
    private const int MaxDisplayNameLength = 100;

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").WithTags("Mi cuenta");

        group.MapGet("/account", GetAsync)
            .WithSummary("Mi cuenta: usuario, nombre visible y si soy administrador.");

        group.MapPut("/account", UpdateAsync)
            .WithSummary("Cambia mi nombre visible.");

        group.MapPut("/password", ChangePasswordAsync)
            .WithSummary("Cambia mi contraseña con la actual y la nueva (400 si la actual no es correcta). Mi sesión sigue; las demás se cierran.");

        return app;
    }

    private static async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult>> GetAsync(
        HttpContext http, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(http.User);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(await AuthEndpoints.ToResponseAsync(user, users));
    }

    private static async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult, ValidationProblem>> UpdateAsync(
        UpdateAccountRequest request, HttpContext http, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var displayName = request.DisplayName?.Trim() ?? "";
        if (displayName.Length == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["displayName"] = ["Escribe tu nombre."],
            });
        }

        if (displayName.Length > MaxDisplayNameLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["displayName"] = [$"El nombre puede tener hasta {MaxDisplayNameLength} caracteres."],
            });
        }

        user.DisplayName = displayName;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return TypedResults.ValidationProblem(IdentityErrors.ToValidation(updated));
        }

        return TypedResults.Ok(await AuthEndpoints.ToResponseAsync(user, users));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(request.CurrentPassword))
        {
            errors["currentPassword"] = ["Escribe tu contraseña actual."];
        }

        if (string.IsNullOrEmpty(request.NewPassword))
        {
            errors["newPassword"] = ["Escribe la contraseña nueva."];
        }
        else if (request.NewPassword == request.CurrentPassword)
        {
            errors["newPassword"] = ["La contraseña nueva tiene que ser distinta de la actual."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Comprueba la actual, valida la nueva con las reglas de Identity y renueva el sello de seguridad: las
        // demás sesiones de este usuario dejan de valer.
        var changed = await users.ChangePasswordAsync(user, request.CurrentPassword!, request.NewPassword!);
        if (!changed.Succeeded)
        {
            // Un 400 y no un 401: Angular cierra la sesión ante un 401, y aquí la sesión es buena.
            return TypedResults.ValidationProblem(IdentityErrors.ToValidation(changed, "newPassword"));
        }

        // Vuelve a emitir la cookie de esta sesión con el sello nuevo, para que esta sí siga valiendo.
        await signIn.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }
}
