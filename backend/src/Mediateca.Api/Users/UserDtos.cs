namespace Mediateca.Api.Users;

/// <summary>
/// Usuario para el administrador. <c>Role</c> es <c>Admin</c> o <c>Usuario</c>. <c>LibraryIds</c> son las
/// bibliotecas que puede ver; en un administrador siempre va vacía, porque ve todas sin necesitar permisos.
/// </summary>
public sealed record AdminUserResponse(
    Guid Id,
    string UserName,
    string DisplayName,
    string Role,
    IReadOnlyList<Guid> LibraryIds,
    DateTimeOffset CreatedAt);

/// <summary>
/// Cuerpo de POST /api/admin/users. El rol es <c>Admin</c> o <c>Usuario</c> (por omisión, <c>Usuario</c>).
/// Las bibliotecas se ignoran si el rol es <c>Admin</c>.
/// </summary>
public sealed record CreateUserRequest(
    string? UserName,
    string? DisplayName,
    string? Password,
    string? Role,
    List<Guid>? LibraryIds);

/// <summary>
/// Cuerpo de PUT /api/admin/users/{id}. El nombre de usuario no cambia. Sin <c>LibraryIds</c>, las bibliotecas
/// se quedan como están; con el rol <c>Admin</c> se ignoran (y se borran las que tuviera).
/// </summary>
public sealed record UpdateUserRequest(string? DisplayName, string? Role, List<Guid>? LibraryIds);

/// <summary>Cuerpo de PUT /api/admin/users/{id}/libraries.</summary>
public sealed record SetUserLibrariesRequest(List<Guid>? LibraryIds);

/// <summary>Cuerpo de PUT /api/admin/users/{id}/password: la contraseña nueva que pone el administrador.</summary>
public sealed record ResetPasswordRequest(string? NewPassword);

/// <summary>Cuerpo de PUT /api/me/account.</summary>
public sealed record UpdateAccountRequest(string? DisplayName);

/// <summary>Cuerpo de PUT /api/me/password.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
