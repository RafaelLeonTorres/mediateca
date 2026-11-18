namespace Mediateca.Infrastructure.Metadata;

/// <summary>Sección "Metadata" de la configuración (PLAN.md, sección 14).</summary>
public sealed class MetadataOptions
{
    public const string Section = "Metadata";

    public OmdbOptions Omdb { get; set; } = new();

    public AniListOptions AniList { get; set; } = new();

    /// <summary>
    /// Cuánto vale una respuesta guardada en la caché (<c>MetadataCache</c>) antes de volver a pedirla. Con
    /// "forzar" (el botón Actualizar metadatos y el refresco de las series en emisión) se ignora.
    /// </summary>
    public TimeSpan CacheMaxAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Cada cuánto busca el worker elementos pendientes cuando la cola está vacía.</summary>
    public TimeSpan QueuePollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Cada cuánto vuelve a pedir los datos de una serie en emisión (siguiente episodio y títulos nuevos).</summary>
    public TimeSpan AiringRefreshInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Cada cuánto revisa el worker si hay series que refrescar (la tarea de Quartz). Solo decide con qué rapidez se entera.</summary>
    public TimeSpan RefreshCheckInterval { get; set; } = TimeSpan.FromHours(1);
}

public sealed class OmdbOptions
{
    /// <summary>Clave gratuita de https://www.omdbapi.com/apikey.aspx (<c>OMDB_API_KEY</c> en .env). Sin ella, OMDb queda desactivado.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://www.omdbapi.com/";

    /// <summary>Espera mínima entre dos solicitudes. OMDb no limita por segundo, solo por día (1,000 con la clave gratuita).</summary>
    public TimeSpan RequestInterval { get; set; } = TimeSpan.FromMilliseconds(250);
}

public sealed class AniListOptions
{
    public string Endpoint { get; set; } = "https://graphql.anilist.co";

    /// <summary>Espera mínima entre dos solicitudes: 2 s son 30 por minuto, el límite actual (modo degradado) de AniList.</summary>
    public TimeSpan RequestInterval { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>Nombres de los <c>HttpClient</c> con nombre de los proveedores (las pruebas les ponen un manejador falso).</summary>
public static class HttpClientNames
{
    public const string AniList = "anilist";
    public const string Omdb = "omdb";
    public const string Images = "images";
}
