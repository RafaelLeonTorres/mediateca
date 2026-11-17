using Microsoft.Win32;

namespace Mediateca.Tray;

/// <summary>Registra o quita el arranque de la app al iniciar sesión en Windows (clave Run del usuario).</summary>
public static class AutostartRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Mediateca";

    /// <summary>Arma el comando que se guarda en el registro: <c>"exe" --carpeta "carpeta" --inicio</c>.</summary>
    /// <param name="processPath">Ruta del ejecutable de la app.</param>
    /// <param name="composeFolder">Carpeta con docker-compose.yml.</param>
    /// <returns>El comando completo.</returns>
    public static string BuildCommand(string processPath, string composeFolder)
    {
        return "\"" + processPath + "\" --carpeta \"" + composeFolder + "\" --inicio";
    }

    /// <summary>Indica si existe el valor <c>Mediateca</c> en la clave Run.</summary>
    /// <returns><c>true</c> si el arranque con Windows está activado.</returns>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>Activa el arranque con Windows para la carpeta dada.</summary>
    /// <param name="composeFolder">Carpeta con docker-compose.yml.</param>
    /// <exception cref="InvalidOperationException">Si la app corre con <c>dotnet</c> y no desde su .exe publicado.</exception>
    public static void Enable(string composeFolder)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath)
            || string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Publica la app con bandeja.ps1 para poder iniciarla con Windows.");
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, BuildCommand(processPath, composeFolder), RegistryValueKind.String);
    }

    /// <summary>Quita el arranque con Windows; no hace nada si no estaba activado.</summary>
    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.DeleteValue(ValueName, false);
    }
}
