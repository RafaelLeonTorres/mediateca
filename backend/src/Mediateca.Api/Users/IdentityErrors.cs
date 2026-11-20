using Microsoft.AspNetCore.Identity;

namespace Mediateca.Api.Users;

/// <summary>Convierte los errores de Identity (ya en español) en los de un <c>ValidationProblem</c>, agrupados por campo.</summary>
internal static class IdentityErrors
{
    /// <param name="result">Resultado fallido de Identity.</param>
    /// <param name="passwordField">Campo del cuerpo donde va la contraseña nueva ("password" al crear, "newPassword" al cambiarla).</param>
    public static Dictionary<string, string[]> ToValidation(IdentityResult result, string passwordField = "password") =>
        result.Errors
            .GroupBy(error => FieldOf(error.Code, passwordField))
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray());

    private static string FieldOf(string code, string passwordField) =>
        code switch
        {
            // La contraseña actual equivocada, al cambiarla.
            "PasswordMismatch" => "currentPassword",
            _ when code.StartsWith("Password", StringComparison.Ordinal) => passwordField,
            "DuplicateUserName" or "InvalidUserName" => "userName",
            _ => "user",
        };
}
