using System.Security.Claims;
using Mediateca.Api.Auth;
using Mediateca.Core.Users;
using Mediateca.Infrastructure.Identity;
using Mediateca.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mediateca.Api.Users;

/// <summary>
/// Administración de usuarios (PLAN.md, sección 6). Solo para administradores. No hay registro público: las
/// cuentas las crea el administrador, con su rol (Admin o Usuario) y las bibliotecas que puede ver. Un
/// administrador ve todas las bibliotecas, así que sus permisos por biblioteca no se guardan ni cuentan.
/// </summary>
public static class AdminUserEndpoints
{
    private const int MaxUserNameLength = 50;
    private const int MaxDisplayNameLength = 100;

    // Llave del candado de PostgreSQL que serializa lo que puede dejar al sistema sin administradores.
    private const long AdminChangeLockKey = 4343;

    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .WithTags("Administración de usuarios")
            .RequireAuthorization(AuthSetup.AdminPolicy);

        group.MapGet("", ListAsync)
            .WithSummary("Lista los usuarios con su rol, sus bibliotecas permitidas y su fecha de alta.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Un usuario.");

        group.MapPost("", CreateAsync)
            .WithSummary("Crea un usuario con su contraseña inicial, su rol y sus bibliotecas. Las bibliotecas de un administrador no cuentan.");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithSummary("Cambia el nombre visible, el rol y las bibliotecas. Cambiar el rol cierra sus sesiones. Un administrador no puede quitarse el rol y siempre queda al menos uno (409).");

        group.MapPut("/{id:guid}/libraries", SetLibrariesAsync)
            .WithSummary("Reemplaza las bibliotecas que puede ver un usuario. No aplica a un administrador, que ve todas.");

        group.MapPut("/{id:guid}/password", ResetPasswordAsync)
            .WithSummary("Pone una contraseña nueva a un usuario (la elige el administrador) y cierra sus sesiones.");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Borra un usuario con su progreso y sus permisos, y cierra sus sesiones. Un administrador no puede borrarse y siempre queda al menos uno (409).");

        return app;
    }

    private static async Task<Ok<List<AdminUserResponse>>> ListAsync(
        MediatecaDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await AdminUserQueries.LoadAsync(db, null, cancellationToken));

    private static async Task<Results<Ok<AdminUserResponse>, NotFound>> GetAsync(
        Guid id, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var user = (await AdminUserQueries.LoadAsync(db, id, cancellationToken)).FirstOrDefault();
        return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
    }

    private static async Task<Results<Created<AdminUserResponse>, ValidationProblem>> CreateAsync(
        CreateUserRequest request,
        MediatecaDbContext db,
        UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var userName = ValidateUserName(request.UserName, errors);
        var displayName = ValidateDisplayName(request.DisplayName, errors);
        var role = ValidateRole(string.IsNullOrWhiteSpace(request.Role) ? Roles.Usuario : request.Role, errors);
        if (string.IsNullOrEmpty(request.Password))
        {
            errors["password"] = ["Escribe una contraseña inicial."];
        }

        var libraryIds = await ResolveLibrariesAsync(db, request.LibraryIds, errors, cancellationToken);
        if (role is not null && !await roles.RoleExistsAsync(role))
        {
            errors["role"] = [$"El rol '{role}' no existe en el servidor."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Todo o nada: si falla el rol o los permisos, el usuario no queda creado.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new AppUser { UserName = userName, DisplayName = displayName, CreatedAt = DateTimeOffset.UtcNow };

        var created = await users.CreateAsync(user, request.Password!);
        if (!created.Succeeded)
        {
            return TypedResults.ValidationProblem(IdentityErrors.ToValidation(created));
        }

        var inRole = await users.AddToRoleAsync(user, role!);
        if (!inRole.Succeeded)
        {
            return TypedResults.ValidationProblem(IdentityErrors.ToValidation(inRole));
        }

        // Un administrador ve todo: no se le guardan permisos por biblioteca.
        if (role != Roles.Admin && libraryIds is { Count: > 0 })
        {
            db.LibraryAccess.AddRange(libraryIds.Select(id => new UserLibraryAccess { UserId = user.Id, LibraryId = id }));
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var response = (await AdminUserQueries.LoadAsync(db, user.Id, cancellationToken)).Single();
        return TypedResults.Created($"/api/admin/users/{user.Id}", response);
    }

    private static async Task<Results<Ok<AdminUserResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        ClaimsPrincipal actor,
        MediatecaDbContext db,
        UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        CancellationToken cancellationToken)
    {
        var target = await users.FindByIdAsync(id.ToString());
        if (target is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        var displayName = ValidateDisplayName(request.DisplayName, errors);
        var role = ValidateRole(request.Role, errors);
        var libraryIds = await ResolveLibrariesAsync(db, request.LibraryIds, errors, cancellationToken);
        if (role is not null && !await roles.RoleExistsAsync(role))
        {
            errors["role"] = [$"El rol '{role}' no existe en el servidor."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Quitar el rol de administrador puede dejar al sistema sin ninguno: los cambios que podrían hacerlo se
        // atienden de uno en uno, y la comprobación se hace con el candado puesto.
        if (role != Roles.Admin)
        {
            await LockAdminChangesAsync(db, cancellationToken);
            if (await users.IsInRoleAsync(target, Roles.Admin))
            {
                if (target.Id == actor.GetId())
                {
                    return SelfDemotion();
                }

                if (await CountOtherAdminsAsync(users, target) == 0)
                {
                    return LastAdmin();
                }
            }
        }

        target.DisplayName = displayName;
        var updated = await users.UpdateAsync(target);
        if (!updated.Succeeded)
        {
            return TypedResults.ValidationProblem(IdentityErrors.ToValidation(updated));
        }

        if (!await users.IsInRoleAsync(target, role!))
        {
            var changed = await SetRoleAsync(users, target, role!);
            if (!changed.Succeeded)
            {
                return TypedResults.ValidationProblem(IdentityErrors.ToValidation(changed));
            }

            // Con otro rol, las sesiones abiertas dejan de valer: la cookie lleva el rol y el sello de seguridad.
            var stamped = await users.UpdateSecurityStampAsync(target);
            if (!stamped.Succeeded)
            {
                return TypedResults.ValidationProblem(IdentityErrors.ToValidation(stamped));
            }
        }

        if (role == Roles.Admin)
        {
            await db.LibraryAccess.Where(a => a.UserId == target.Id).ExecuteDeleteAsync(cancellationToken);
        }
        else if (libraryIds is not null)
        {
            await ReplaceLibrariesAsync(db, target.Id, libraryIds, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok((await AdminUserQueries.LoadAsync(db, target.Id, cancellationToken)).Single());
    }

    private static async Task<Results<Ok<AdminUserResponse>, NotFound, ValidationProblem>> SetLibrariesAsync(
        Guid id,
        SetUserLibrariesRequest request,
        MediatecaDbContext db,
        UserManager<AppUser> users,
        CancellationToken cancellationToken)
    {
        var target = await users.FindByIdAsync(id.ToString());
        if (target is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        if (request.LibraryIds is null)
        {
            errors["libraryIds"] = ["Manda la lista de bibliotecas (puede estar vacía)."];
        }
        else if (await users.IsInRoleAsync(target, Roles.Admin))
        {
            errors["libraryIds"] = ["Un administrador ve todas las bibliotecas: no hace falta asignárselas."];
        }

        var libraryIds = await ResolveLibrariesAsync(db, request.LibraryIds, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ReplaceLibrariesAsync(db, target.Id, libraryIds!, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok((await AdminUserQueries.LoadAsync(db, target.Id, cancellationToken)).Single());
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> ResetPasswordAsync(
        Guid id,
        ResetPasswordRequest request,
        ClaimsPrincipal actor,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        var target = await users.FindByIdAsync(id.ToString());
        if (target is null)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrEmpty(request.NewPassword))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["newPassword"] = ["Escribe la contraseña nueva."],
            });
        }

        // ResetPasswordAsync valida la contraseña con las reglas de Identity, guarda el hash y renueva el sello de
        // seguridad: las sesiones abiertas de ese usuario dejan de valer.
        var token = await users.GeneratePasswordResetTokenAsync(target);
        var reset = await users.ResetPasswordAsync(target, token, request.NewPassword);
        if (!reset.Succeeded)
        {
            return TypedResults.ValidationProblem(IdentityErrors.ToValidation(reset, "newPassword"));
        }

        // Si estaba bloqueado por intentos fallidos, con la contraseña nueva puede entrar de inmediato.
        await users.ResetAccessFailedCountAsync(target);
        await users.SetLockoutEndDateAsync(target, null);

        // Si el administrador se la cambia a sí mismo, su sesión actual sigue valiendo.
        if (target.Id == actor.GetId())
        {
            await signIn.RefreshSignInAsync(target);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ClaimsPrincipal actor,
        MediatecaDbContext db,
        UserManager<AppUser> users,
        CancellationToken cancellationToken)
    {
        var target = await users.FindByIdAsync(id.ToString());
        if (target is null)
        {
            return TypedResults.NotFound();
        }

        if (target.Id == actor.GetId())
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "No puedes borrar tu propia cuenta.",
                detail: "Pídele a otro administrador que lo haga.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAdminChangesAsync(db, cancellationToken);
        if (await users.IsInRoleAsync(target, Roles.Admin) && await CountOtherAdminsAsync(users, target) == 0)
        {
            return LastAdmin();
        }

        // El progreso, los permisos y los roles del usuario se borran en cascada en la base de datos. Su cookie deja
        // de valer porque el usuario ya no existe.
        var deleted = await users.DeleteAsync(target);
        if (!deleted.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: string.Join(" ", deleted.Errors.Select(e => e.Description)));
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static string ValidateUserName(string? requested, Dictionary<string, string[]> errors)
    {
        var userName = requested?.Trim() ?? "";
        if (userName.Length == 0)
        {
            errors["userName"] = ["Escribe un nombre de usuario."];
        }
        else if (userName.Length > MaxUserNameLength)
        {
            errors["userName"] = [$"El usuario puede tener hasta {MaxUserNameLength} caracteres."];
        }

        return userName;
    }

    private static string ValidateDisplayName(string? requested, Dictionary<string, string[]> errors)
    {
        var displayName = requested?.Trim() ?? "";
        if (displayName.Length == 0)
        {
            errors["displayName"] = ["Escribe el nombre que se mostrará."];
        }
        else if (displayName.Length > MaxDisplayNameLength)
        {
            errors["displayName"] = [$"El nombre puede tener hasta {MaxDisplayNameLength} caracteres."];
        }

        return displayName;
    }

    /// <summary>El rol con su escritura oficial ("admin" pasa a "Admin"), o null con un error si no es ninguno de los dos.</summary>
    private static string? ValidateRole(string? requested, Dictionary<string, string[]> errors)
    {
        var role = Roles.All.FirstOrDefault(r => string.Equals(r, requested?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            errors["role"] = [$"Elige el rol: {Roles.Admin} o {Roles.Usuario}."];
        }

        return role;
    }

    /// <summary>Las bibliotecas pedidas, sin repetidas. Null si no se pidió ninguna lista; con un error si alguna no existe.</summary>
    private static async Task<List<Guid>?> ResolveLibrariesAsync(
        MediatecaDbContext db, List<Guid>? requested, Dictionary<string, string[]> errors, CancellationToken cancellationToken)
    {
        if (requested is null)
        {
            return null;
        }

        var ids = requested.Distinct().ToList();
        if (ids.Count > 0)
        {
            var existing = await db.Libraries.Where(l => ids.Contains(l.Id)).CountAsync(cancellationToken);
            if (existing != ids.Count)
            {
                errors["libraryIds"] = ["Alguna de las bibliotecas elegidas ya no existe. Recarga la página."];
            }
        }

        return ids;
    }

    /// <summary>Deja como permisos del usuario exactamente estas bibliotecas. Se llama dentro de una transacción.</summary>
    private static async Task ReplaceLibrariesAsync(
        MediatecaDbContext db, Guid userId, IReadOnlyCollection<Guid> libraryIds, CancellationToken cancellationToken)
    {
        await db.LibraryAccess.Where(a => a.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        if (libraryIds.Count == 0)
        {
            return;
        }

        db.LibraryAccess.AddRange(libraryIds.Select(id => new UserLibraryAccess { UserId = userId, LibraryId = id }));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Cambia el rol de la app del usuario (Admin o Usuario) sin tocar otros roles que pudiera tener.</summary>
    private static async Task<IdentityResult> SetRoleAsync(UserManager<AppUser> users, AppUser user, string role)
    {
        var current = await users.GetRolesAsync(user);
        var toRemove = current.Where(name => name != role && Roles.All.Contains(name)).ToList();
        if (toRemove.Count > 0)
        {
            var removed = await users.RemoveFromRolesAsync(user, toRemove);
            if (!removed.Succeeded)
            {
                return removed;
            }
        }

        return current.Contains(role) ? IdentityResult.Success : await users.AddToRoleAsync(user, role);
    }

    /// <summary>Candado de la transacción: dos cambios que podrían quitar administradores no se comprueban a la vez.</summary>
    private static Task<int> LockAdminChangesAsync(MediatecaDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdminChangeLockKey})", cancellationToken);

    private static async Task<int> CountOtherAdminsAsync(UserManager<AppUser> users, AppUser target) =>
        (await users.GetUsersInRoleAsync(Roles.Admin)).Count(admin => admin.Id != target.Id);

    private static ProblemHttpResult SelfDemotion() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "No puedes quitarte el rol de administrador.",
            detail: "Pídele a otro administrador que lo haga.");

    private static ProblemHttpResult LastAdmin() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Tiene que quedar al menos un administrador.",
            detail: "Nombra a otro administrador antes de quitar este.");
}
