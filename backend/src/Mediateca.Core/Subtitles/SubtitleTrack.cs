namespace Mediateca.Core.Subtitles;

/// <summary>De dónde sale una pista de subtítulos.</summary>
public enum SubtitleOrigin
{
    /// <summary>Dentro del contenedor del video (MP4, MKV...).</summary>
    Embedded,

    /// <summary>Un archivo suelto junto al video (<c>.srt</c>, <c>.vtt</c>, <c>.ass</c>, <c>.ssa</c>).</summary>
    External,
}

/// <summary>
/// Una pista de subtítulos de texto de un video. <c>Id</c> es estable mientras el archivo no cambie:
/// <c>e2</c> para el stream 2 del contenedor, <c>x0</c> para el primer archivo suelto. <c>StreamIndex</c> y
/// <c>FilePath</c> son internos y nunca se mandan al cliente. <c>Label</c> la pone
/// <see cref="SubtitleLabels"/>.
/// </summary>
public sealed record SubtitleTrack(
    string Id,
    string Label,
    string? Language,
    string Codec,
    SubtitleOrigin Origin,
    bool IsDefault,
    bool IsForced,
    string? Title,
    int? StreamIndex,
    string? FilePath);
