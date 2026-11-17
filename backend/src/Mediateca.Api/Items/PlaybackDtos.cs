namespace Mediateca.Api.Items;

/// <summary>Cuerpo de <c>PUT /api/items/{id}/progress</c>: dónde va el usuario, en segundos.</summary>
public sealed record ProgressRequest(double? PositionSeconds);

/// <summary>
/// Lo que necesita el reproductor de video: de dónde sale el archivo, cómo se llama, dónde se quedó el
/// usuario y qué hay antes y después dentro de la misma carpeta. Nunca incluye la ruta del archivo.
/// <c>SeriesTitle</c> y <c>ParentTitle</c> son el contexto del título ("Serie · Lat · Episodio 3"):
/// la serie, y la temporada, la carpeta o el álbum. <c>PreviousId</c> y <c>NextId</c> son los vecinos
/// con número dentro del mismo padre, en el orden de la lista de hijos; las carpetas "Lat" y "Sub"
/// nunca se mezclan.
/// </summary>
public sealed record PlaybackInfo(
    Guid Id,
    string Kind,
    string Title,
    string? SeriesTitle,
    string? ParentTitle,
    string? Artist,
    Guid? ParentId,
    string StreamUrl,
    string ContentType,
    bool IsVideo,
    double? DurationSeconds,
    double PositionSeconds,
    bool Played,
    Guid? PreviousId,
    Guid? NextId,
    string? NextTitle);

/// <summary>
/// Una película o episodio para las tarjetas de "Continuar viendo" y "A continuación": con el título de
/// la serie y de su carpeta, la duración y la posición para la barra de avance. <c>ImageId</c> es el
/// elemento cuya imagen se muestra (<c>/api/items/{ImageId}/image</c>): el propio, o su temporada o
/// serie cuando no tiene miniatura; null si ninguno tiene imagen.
/// </summary>
public sealed record ResumeItem(
    Guid Id,
    string Kind,
    string Title,
    string? SeriesTitle,
    string? ParentTitle,
    Guid? ImageId,
    double? DurationSeconds,
    double PositionSeconds,
    DateTimeOffset? LastPlayedAt);
