using Microsoft.AspNetCore.Identity;

namespace Mediateca.Infrastructure.Identity;

/// <summary>Usuario de la app. Identity guarda el hash de la contraseña, el bloqueo por intentos y los roles.</summary>
public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
