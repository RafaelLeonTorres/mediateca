using Mediateca.Core.Subtitles;

namespace Mediateca.Api.Items;

/// <summary>
/// Una pista de subtítulos de texto de un video, como la ve el reproductor. <c>Id</c> es estable (<c>e2</c> para el
/// stream 2 del contenedor, <c>x0</c> para el primer archivo suelto), <c>Language</c> es el código de 2 letras
/// cuando se conoce, <c>Source</c> es <c>embedded</c> o <c>external</c> y <c>Url</c> apunta al WebVTT. Nunca incluye
/// rutas del servidor.
/// </summary>
public sealed record SubtitleTrackResponse(
    string Id,
    string Label,
    string? Language,
    string Codec,
    string Source,
    bool IsDefault,
    bool IsForced,
    string Url)
{
    public static SubtitleTrackResponse From(Guid itemId, SubtitleTrack track) =>
        new(
            track.Id,
            track.Label,
            track.Language,
            track.Codec,
            track.Origin == SubtitleOrigin.Embedded ? "embedded" : "external",
            track.IsDefault,
            track.IsForced,
            $"/api/items/{itemId}/subtitles/{track.Id}.vtt");
}
