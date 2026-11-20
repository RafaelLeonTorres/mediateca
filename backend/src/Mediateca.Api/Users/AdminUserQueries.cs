using Mediateca.Infrastructure.Identity;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mediateca.Api.Users;

/// <summary>Lectura de usuarios para el administrador: con su rol y sus bibliotecas, en pocas consultas.</summary>
internal static class AdminUserQueries
{
    /// <summary>Todos los usuarios por nombre de usuario, o solo el indicado.</summary>
    public static async Task<List<AdminUserResponse>> LoadAsync(
        MediatecaDbContext db, Guid? userId, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking();
        if (userId is { } id)
        {
            query = query.Where(u => u.Id == id);
        }

        var rows = await query.OrderBy(u => u.NormalizedUserName).ThenBy(u => u.Id).ToListAsync(cancellationToken);
        var ids = rows.Select(u => u.Id).ToList();

        var adminRoleId = await db.Roles.AsNoTracking()
            .Where(r => r.Name == Roles.Admin)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var admins = (await db.UserRoles.AsNoTracking()
                .Where(ur => ur.RoleId == adminRoleId && ids.Contains(ur.UserId))
                .Select(ur => ur.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var access = (await db.LibraryAccess.AsNoTracking()
                .Where(a => ids.Contains(a.UserId))
                .OrderBy(a => a.LibraryId)
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.UserId, a => a.LibraryId);

        return rows
            .Select(user =>
            {
                var isAdmin = admins.Contains(user.Id);
                return new AdminUserResponse(
                    user.Id,
                    user.UserName ?? "",
                    user.DisplayName,
                    isAdmin ? Roles.Admin : Roles.Usuario,
                    // Un administrador ve todo: sus permisos por biblioteca no cuentan.
                    isAdmin ? new List<Guid>() : access[user.Id].ToList(),
                    user.CreatedAt);
            })
            .ToList();
    }
}
