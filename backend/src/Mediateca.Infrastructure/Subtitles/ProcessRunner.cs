using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Mediateca.Core.Subtitles;

namespace Mediateca.Infrastructure.Subtitles;

/// <summary>
/// Ejecuta un programa sin shell (<c>ProcessStartInfo.ArgumentList</c>: cada argumento va aparte, así un
/// nombre de archivo con espacios o comillas no se interpreta). Si se cancela la solicitud o se pasa del
/// tiempo límite, mata el proceso y su árbol.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return ProcessResult.NotInstalled;
        }

        // Ninguno de los dos programas lee la entrada: se cierra para que nunca se queden esperándola.
        process.StandardInput.Close();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(request.Timeout);
        try
        {
            // La salida se lee mientras corre: si no, el programa se bloquea al llenarse el búfer.
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            return new ProcessResult(ProcessOutcome.Completed, process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return ProcessResult.TimedOut;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Ya había terminado.
        }
    }
}
