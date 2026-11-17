namespace Mediateca.Infrastructure.Identity;

/// <summary>Roles de la app. Se siembran en el primer arranque.</summary>
public static class Roles
{
    /// <summary>Administra bibliotecas y usuarios, y ve todas las bibliotecas.</summary>
    public const string Admin = "Admin";

    /// <summary>Ve solo las bibliotecas que le asigna el administrador.</summary>
    public const string Usuario = "Usuario";

    public static IReadOnlyList<string> All { get; } = [Admin, Usuario];
}
