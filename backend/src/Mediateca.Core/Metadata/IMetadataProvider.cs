using Mediateca.Core.Libraries;

namespace Mediateca.Core.Metadata;

/// <summary>Nombres de los proveedores, que son también la llave de la caché (<c>MetadataCache.Provider</c>).</summary>
public static class MetadataProviders
{
    public const string AniList = "anilist";
    public const string Omdb = "omdb";

    /// <summary>Proveedor de cada tipo de biblioteca: AniList para el anime y OMDb (IMDb) para películas y series. La música no tiene.</summary>
    public static string? For(LibraryType type) => type switch
    {
        LibraryType.Anime => AniList,
        LibraryType.Movies or LibraryType.Series => Omdb,
        _ => null,
    };

    /// <summary>Nombre para mostrar: "AniList" u "OMDb".</summary>
    public static string DisplayName(string provider) => provider == AniList ? "AniList" : "OMDb";
}

/// <summary>
/// Fuente de metadatos (PLAN.md, sección 8). Las respuestas se guardan en la caché; con <c>refresh</c> se
/// ignora lo guardado y se vuelve a preguntar.
/// </summary>
public interface IMetadataProvider
{
    /// <summary>"anilist" u "omdb".</summary>
    string Name { get; }

    /// <summary>Si puede usarse: AniList siempre; OMDb solo con clave.</summary>
    bool IsConfigured { get; }

    /// <summary>Hasta cuándo no se le deben pedir datos (límite de solicitudes o límite diario). Nulo si está disponible.</summary>
    DateTimeOffset? PausedUntil { get; }

    bool Supports(LibraryType type);

    Task<IReadOnlyList<SearchCandidate>> SearchAsync(SearchQuery query, bool refresh, CancellationToken cancellationToken);

    /// <summary>Datos de un elemento por su id (el de AniList o el de IMDb). Nulo si el proveedor no lo conoce.</summary>
    Task<ProviderMetadata?> GetDetailsAsync(string externalId, bool refresh, CancellationToken cancellationToken);

    /// <summary>Datos de un resultado de búsqueda: los que ya traía o, si no, una solicitud de detalle.</summary>
    Task<ProviderMetadata?> GetDetailsAsync(SearchCandidate candidate, bool refresh, CancellationToken cancellationToken);

    /// <summary>Títulos de episodios de las temporadas indicadas (en AniList, los del propio detalle).</summary>
    Task<IReadOnlyList<EpisodeTitle>> GetEpisodeTitlesAsync(
        ProviderMetadata details, IReadOnlyCollection<int> seasons, bool refresh, CancellationToken cancellationToken);
}

/// <summary>Respuestas guardadas de los proveedores: la tabla <c>MetadataCache</c>. Guarda el JSON tal como llegó.</summary>
public interface IMetadataCache
{
    /// <summary>El JSON guardado, o null si no hay o tiene más de <paramref name="maxAge"/>.</summary>
    Task<string?> GetAsync(string provider, string key, TimeSpan maxAge, CancellationToken cancellationToken);

    Task SetAsync(string provider, string key, string payload, CancellationToken cancellationToken);
}

/// <summary>Un proveedor no pudo dar los datos. El mensaje está en español y se muestra tal cual al administrador.</summary>
public class MetadataProviderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>El proveedor pidió esperar (HTTP 429 o bloqueo): hay que volver a intentar pasado <see cref="RetryAfter"/>.</summary>
public sealed class ProviderRateLimitedException(string provider, TimeSpan retryAfter)
    : MetadataProviderException($"{MetadataProviders.DisplayName(provider)} pidió esperar {Format(retryAfter)} antes de más solicitudes.")
{
    public string Provider { get; } = provider;

    public TimeSpan RetryAfter { get; } = retryAfter;

    private static string Format(TimeSpan time) =>
        time.TotalMinutes >= 1 ? $"{Math.Ceiling(time.TotalMinutes):0} min" : $"{Math.Ceiling(time.TotalSeconds):0} s";
}

/// <summary>OMDb llegó a su límite diario de solicitudes: no se le pide nada más hasta el día siguiente.</summary>
public sealed class ProviderQuotaExceededException(DateTimeOffset resumesAt)
    : MetadataProviderException("OMDb llegó a su límite diario de solicitudes. La cola sigue mañana.")
{
    public DateTimeOffset ResumesAt { get; } = resumesAt;
}

/// <summary>Falta la clave del proveedor (OMDb) o el proveedor la rechazó.</summary>
public sealed class ProviderNotConfiguredException(string provider, string? message = null)
    : MetadataProviderException(message ?? (provider == MetadataProviders.Omdb
        ? "Falta la clave de OMDb (OMDB_API_KEY)."
        : $"{MetadataProviders.DisplayName(provider)} no está configurado."))
{
    public string Provider { get; } = provider;
}
