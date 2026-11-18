using Mediateca.Core.Subtitles;

namespace Mediateca.Api.Items;

/// <summary>
/// Una pista de subtítulos de texto de un video, como la ve el reproductor. <c>Id</c> es estable (<c>e2</c> para el
/// stream 2 del contenedor, <c>x0</c> para el primer archivo suelto, <c>u…</c> para uno subido), <c>Language</c> es el
/// código de 2 letras cuando se conoce, <c>Source</c> es <c>embedded</c>, <c>external</c> o <c>uploaded</c>, <c>Url</c>
/// apunta al WebVTT y <c>CanDelete</c> dice si quien pregunta puede borrarla (solo las subidas, por quien las subió
/// o un administrador). Nunca incluye rutas del servidor ni quién la subió.
/// </summary>
public sealed record SubtitleTrackResponse(
    string Id,
    string Label,
    string? Language,
    string Codec,
    string Source,
    bool IsDefault,
    bool IsForced,
    string Url,
    bool CanDelete)
{
    public static SubtitleTrackResponse From(Guid itemId, SubtitleTrack track, bool canDelete = false) =>
        new(
            track.Id,
            track.Label,
            track.Language,
            track.Codec,
            track.Origin switch
            {
                SubtitleOrigin.Embedded => "embedded",
                SubtitleOrigin.External => "external",
                _ => "uploaded",
            },
            track.IsDefault,
            track.IsForced,
            $"/api/items/{itemId}/subtitles/{track.Id}.vtt",
            canDelete && track.Origin == SubtitleOrigin.Uploaded);
}
