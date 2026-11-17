namespace Mediateca.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Una sola instancia por sesión de Windows: si ya hay una, esta sale sin avisar.
        using var mutex = new Mutex(true, "Local\\Mediateca.Tray", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        var folder = ComposeFolder.Resolve(args);
        if (folder is null)
        {
            MessageBox.Show(
                "No encontré la carpeta de Mediateca (la que tiene docker-compose.yml).\n\n"
                + "Abre PowerShell en la carpeta de Mediateca y ejecuta setup.ps1 (o bandeja.ps1).",
                "Mediateca",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var autoStart = args.Any(a => string.Equals(a, "--inicio", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayContext(folder, autoStart));
    }
}
