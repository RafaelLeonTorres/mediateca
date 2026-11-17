using Mediateca.Core.Libraries;

namespace Mediateca.Api.Items;

/// <summary>Elemento en una cuadrícula o en una fila del inicio. <c>Kind</c>: Movie, Series, MusicAlbum…</summary>
public sealed record ItemSummary(
    Guid Id,
    string Kind,
    string Title,
    int? Year,
    string? Artist,
    bool HasImage);

/// <summary>Página de elementos de una biblioteca.</summary>
public sealed record ItemPage(IReadOnlyList<ItemSummary> Items, int Total, int Page, int PageSize);

/// <summary>Un elemento de la ruta de navegación: la serie o la temporada de un episodio, el álbum de una pista.</summary>
public sealed record ItemReference(Guid Id, string Kind, string Title);

/// <summary>
/// Detalle plano de un elemento de cualquier tipo. <c>Kind</c> dice cuáles de los campos opcionales
/// aplican: temporada y episodio para Season y Episode, artista y pista para MusicAlbum y Track, y
/// duración y tamaño para lo que se reproduce. Nunca incluye la ruta del archivo en el servidor.
/// <c>PositionSeconds</c> y <c>Played</c> son del usuario que pregunta (solo cuentan en lo que se
/// reproduce); <c>UnplayedCount</c> es cuántos episodios le faltan por ver en una serie o temporada.
/// </summary>
public sealed record ItemDetail(
    Guid Id,
    string Kind,
    Guid LibraryId,
    string LibraryName,
    LibraryType LibraryType,
    Guid? ParentId,
    string? ParentTitle,
    IReadOnlyList<ItemReference> Ancestors,
    string Title,
    int? Year,
    string? Overview,
    string? ContentRating,
    double? CommunityRating,
    IReadOnlyList<string> Genres,
    bool HasImage,
    DateTimeOffset DateAdded,
    int? SeasonNumber,
    int? EpisodeNumber,
    int? AbsoluteNumber,
    string? Artist,
    int? TrackNumber,
    int? DiscNumber,
    double? DurationSeconds,
    long? FileSize,
    double PositionSeconds = 0,
    bool Played = false,
    int? UnplayedCount = null);

/// <summary>
/// Hijo de un elemento: temporada o episodio de una serie, episodio de una temporada, pista de un álbum.
/// <c>ChildCount</c> es cuántos episodios tiene una temporada o carpeta ("Lat", "Sub"); en lo demás, null.
/// <c>PositionSeconds</c> y <c>Played</c> son del usuario que pregunta; <c>UnplayedCount</c> es cuántos
/// episodios le faltan por ver en una temporada o carpeta (null en lo demás).
/// </summary>
public sealed record ChildItem(
    Guid Id,
    string Kind,
    string Title,
    int? Year,
    string? Overview,
    bool HasImage,
    int? SeasonNumber,
    int? EpisodeNumber,
    int? AbsoluteNumber,
    string? Artist,
    int? TrackNumber,
    int? DiscNumber,
    double? DurationSeconds,
    int? ChildCount,
    double PositionSeconds = 0,
    bool Played = false,
    int? UnplayedCount = null);
