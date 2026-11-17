using System.Security.Claims;
using Mediateca.Infrastructure.Identity;

namespace Mediateca.Api;

/// <summary>Quién hace la solicitud, a partir de la cookie de sesión.</summary>
internal static class CurrentUser
{
    /// <summary>Id del usuario de la sesión. Guid.Empty si no se puede leer: no coincide con ningún permiso.</summary>
    public static Guid GetId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(Roles.Admin);
}
