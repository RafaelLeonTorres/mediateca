namespace Mediateca.Core.Users;

/// <summary>Reglas del progreso de reproducción (PLAN.md, sección 9).</summary>
public static class PlaybackProgress
{
    /// <summary>Fracción de la duración a partir de la cual un elemento se da por visto.</summary>
    public const double PlayedFraction = 0.9;

    /// <summary>
    /// Indica si la posición pasa del 90 % de la duración. Sin duración conocida nunca se da por visto
    /// aquí: el cliente lo marca cuando el archivo termina.
    /// </summary>
    public static bool IsFinished(double positionSeconds, double? durationSeconds) =>
        durationSeconds is > 0 && positionSeconds > durationSeconds.Value * PlayedFraction;
}
