namespace Mediateca.Infrastructure.Subtitles;

/// <summary>
/// Sección "Media" de la configuración: los programas de FFmpeg que extraen los subtítulos. Por omisión se
/// buscan en el PATH (en Docker, la imagen de la API los instala). Si no están, la app sigue funcionando:
/// solo faltan las pistas incrustadas en los videos.
/// </summary>
public sealed class MediaToolsOptions
{
    public const string Section = "Media";

    /// <summary>Programa que lista las pistas de un archivo (<c>Media:Ffprobe</c>).</summary>
    public string Ffprobe { get; set; } = "ffprobe";

    /// <summary>Programa que extrae una pista a WebVTT (<c>Media:Ffmpeg</c>).</summary>
    public string Ffmpeg { get; set; } = "ffmpeg";

    /// <summary>Extracciones de ffmpeg al mismo tiempo; las demás esperan su turno.</summary>
    public int MaxConcurrentExtractions { get; set; } = 2;

    /// <summary>Tiempo máximo de ffprobe por archivo.</summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Tiempo máximo de ffmpeg por pista.</summary>
    public TimeSpan ExtractionTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
