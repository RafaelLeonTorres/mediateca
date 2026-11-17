namespace Mediateca.Core.Subtitles;

/// <summary>Un programa externo (ffprobe, ffmpeg) con sus argumentos, uno por uno: nunca pasa por un shell.</summary>
public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout);

/// <summary>Cómo terminó un programa externo.</summary>
public enum ProcessOutcome
{
    /// <summary>Corrió hasta el final (con cualquier código de salida).</summary>
    Completed,

    /// <summary>El programa no está instalado (o la ruta configurada no existe).</summary>
    NotInstalled,

    /// <summary>Pasó el tiempo límite y se detuvo.</summary>
    TimedOut,
}

public sealed record ProcessResult(ProcessOutcome Outcome, int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Corrió y terminó con código 0.</summary>
    public bool Succeeded => Outcome == ProcessOutcome.Completed && ExitCode == 0;

    public static ProcessResult NotInstalled { get; } = new(ProcessOutcome.NotInstalled, -1, string.Empty, string.Empty);

    public static ProcessResult TimedOut { get; } = new(ProcessOutcome.TimedOut, -1, string.Empty, string.Empty);
}

/// <summary>
/// Ejecuta programas externos. Está detrás de una interfaz para que las pruebas usen un doble y no
/// dependan de que FFmpeg esté instalado. Si se cancela la solicitud, el programa se detiene y la
/// excepción de cancelación sube; si no está instalado o se pasa del tiempo, lo dice el resultado.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
