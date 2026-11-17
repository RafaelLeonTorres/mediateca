using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Mediateca.Tray;

/// <summary>Ejecuta el CLI de Docker sin ventana y consulta el estado de la pila.</summary>
public static class DockerCli
{
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    /// <summary>
    /// Ejecuta <c>docker</c> con los argumentos dados, sin ventana, leyendo stdout y stderr a la vez.
    /// Si <c>docker</c> no está en el PATH no lanza excepción: devuelve el código -1 y un mensaje en StdErr.
    /// </summary>
    /// <param name="arguments">Argumentos (por ejemplo <c>compose up -d</c>).</param>
    /// <param name="workingDirectory">Carpeta de trabajo; vacía para usar la actual.</param>
    /// <param name="cancellationToken">Al cancelar, se termina el proceso y se lanza <see cref="OperationCanceledException"/>.</param>
    /// <returns>Código de salida, salida estándar y salida de error.</returns>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception ex)
        {
            return (-1, string.Empty, "No se pudo ejecutar docker (¿está instalado y en el PATH?): " + ex.Message);
        }

        if (process is null)
        {
            return (-1, string.Empty, "No se pudo iniciar docker.");
        }

        using (process)
        {
            var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                var stdOut = await stdOutTask;
                var stdErr = await stdErrTask;
                return (process.ExitCode, stdOut, stdErr);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
        }
    }

    /// <summary>Indica si el motor de Docker responde (<c>docker info</c> termina bien y devuelve la versión).</summary>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si Docker está disponible.</returns>
    public static async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync("info --format \"{{.ServerVersion}}\"", string.Empty, cancellationToken);
        return result.ExitCode == 0 && result.StdOut.Trim().Length > 0;
    }

    /// <summary>Espera hasta que Docker responda, revisando cada 3 segundos.</summary>
    /// <param name="timeout">Tiempo máximo de espera.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si Docker respondió antes del límite.</returns>
    public static async Task<bool> WaitUntilAvailableAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (await IsAvailableAsync(cancellationToken))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }

    /// <summary>
    /// Revisa Docker y el estado de los contenedores de la carpeta del compose.
    /// Si los comandos tardan más de 20 segundos, se considera que Docker no está disponible.
    /// </summary>
    /// <param name="composeFolder">Carpeta con docker-compose.yml.</param>
    /// <param name="cancellationToken">Token de cancelación (al salir de la app).</param>
    /// <returns>Estado general y si web está corriendo.</returns>
    public static async Task<StackSnapshot> GetSnapshotAsync(string composeFolder, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            if (!await IsAvailableAsync(timeout.Token))
            {
                return new StackSnapshot(StackState.DockerUnavailable, false);
            }

            var ps = await RunAsync("compose ps --format json", composeFolder, timeout.Token);
            IReadOnlyList<ServiceInfo> services = new List<ServiceInfo>();
            if (ps.ExitCode == 0)
            {
                services = StackStatus.ParsePs(ps.StdOut);
            }

            return new StackSnapshot(
                StackStatus.ComputeState(true, services),
                StackStatus.IsServiceRunning(services, "web"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new StackSnapshot(StackState.DockerUnavailable, false);
        }
    }

    /// <summary>
    /// Elige la línea de error más útil de stderr: la primera que contiene "error" o, si no hay,
    /// la primera no vacía (Compose escribe su progreso en stderr, antes del error real).
    /// </summary>
    /// <param name="stdErr">Salida de error del comando.</param>
    /// <param name="fallback">Texto si stderr está vacío.</param>
    /// <returns>Una sola línea, recortada.</returns>
    public static string FirstErrorLine(string? stdErr, string fallback)
    {
        if (string.IsNullOrWhiteSpace(stdErr))
        {
            return fallback;
        }

        string? firstNonEmpty = null;
        foreach (var raw in stdErr.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            firstNonEmpty ??= line;
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return Shorten(line);
            }
        }

        return firstNonEmpty is null ? fallback : Shorten(firstNonEmpty);
    }

    private static string Shorten(string line)
    {
        return line.Length <= 200 ? line : line.Substring(0, 200) + "…";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception)
        {
            // El proceso ya terminó o no se puede terminar; no hay nada más que hacer.
        }
    }
}
