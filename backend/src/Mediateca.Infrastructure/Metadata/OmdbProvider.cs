using System.Globalization;
using System.Text.Json;
using Mediateca.Core.Libraries;
using Mediateca.Core.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// IMDb a través de OMDb (PLAN.md, sección 8): búsqueda por título y año, detalle con el resumen completo y, para
/// las series, los episodios de cada temporada. Sin clave (<c>Metadata:Omdb:ApiKey</c>) está desactivado y no hace
/// ninguna solicitud. Con "Request limit reached!" (el límite diario) se pausa hasta el día siguiente.
/// </summary>
public sealed class OmdbProvider : IMetadataProvider, IDisposable
{
    private const int MaxSeasons = 40;

    private readonly IHttpClientFactory _http;
    private readonly IMetadataCache _cache;
    private readonly MetadataOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OmdbProvider> _logger;
    private readonly ProviderGate _gate;

    public OmdbProvider(
        IHttpClientFactory http,
        IMetadataCache cache,
        IOptions<MetadataOptions> options,
        TimeProvider time,
        ILogger<OmdbProvider> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _time = time;
        _logger = logger;
        _gate = new ProviderGate(_options.Omdb.RequestInterval, time);
    }

    public string Name => MetadataProviders.Omdb;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Omdb.ApiKey);

    public DateTimeOffset? PausedUntil => _gate.PausedUntil;

    public bool Supports(LibraryType type) => type is LibraryType.Movies or LibraryType.Series;

    public async Task<IReadOnlyList<SearchCandidate>> SearchAsync(
        SearchQuery query, bool refresh, CancellationToken cancellationToken)
    {
        var text = query.Text.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var type = query.Kind switch
        {
            MetadataSearchKind.Movie => "movie",
            MetadataSearchKind.Series => "series",
            _ => null,
        };

        var parameters = new List<(string Name, string Value)> { ("s", text) };
        if (type is not null)
        {
            parameters.Add(("type", type));
        }

        if (query.Year is { } year)
        {
            parameters.Add(("y", year.ToString(CultureInfo.InvariantCulture)));
        }

        var key = MetadataCacheStore.SearchKey($"{text.ToLowerInvariant()}|{query.Year}|{type}");
        var cacheName = Name + "-search";
        if (!refresh && await _cache.GetAsync(cacheName, key, _options.CacheMaxAge, cancellationToken) is { } cached)
        {
            return OmdbParser.ParseSearch(cached);
        }

        var body = await GetAsync(parameters, cancellationToken);
        if (body is null)
        {
            // "Movie not found!" y "Too many results.": sin resultados. Se guarda el vacío para no repetir la búsqueda.
            body = """{"Search":[],"Response":"True"}""";
        }

        await _cache.SetAsync(cacheName, key, body, cancellationToken);
        return OmdbParser.ParseSearch(body);
    }

    public async Task<ProviderMetadata?> GetDetailsAsync(string externalId, bool refresh, CancellationToken cancellationToken)
    {
        var id = externalId.Trim().ToLowerInvariant();
        if (!id.StartsWith("tt", StringComparison.Ordinal))
        {
            return null;
        }

        if (!refresh && await _cache.GetAsync(Name, id, _options.CacheMaxAge, cancellationToken) is { } cached)
        {
            return OmdbParser.ParseDetails(cached);
        }

        var body = await GetAsync([("i", id), ("plot", "full")], cancellationToken);
        if (body is null)
        {
            return null;
        }

        await _cache.SetAsync(Name, id, body, cancellationToken);
        return OmdbParser.ParseDetails(body);
    }

    public Task<ProviderMetadata?> GetDetailsAsync(SearchCandidate candidate, bool refresh, CancellationToken cancellationToken) =>
        GetDetailsAsync(candidate.ExternalId, refresh, cancellationToken);

    public async Task<IReadOnlyList<EpisodeTitle>> GetEpisodeTitlesAsync(
        ProviderMetadata details, IReadOnlyCollection<int> seasons, bool refresh, CancellationToken cancellationToken)
    {
        var titles = new List<EpisodeTitle>();
        if (details.Format != "series")
        {
            return titles;
        }

        foreach (var season in seasons.Where(s => s > 0).Distinct().Order().Take(MaxSeasons))
        {
            var key = $"{details.ExternalId}:{season}";
            var cacheName = Name + "-season";
            var body = !refresh ? await _cache.GetAsync(cacheName, key, _options.CacheMaxAge, cancellationToken) : null;
            if (body is null)
            {
                body = await GetAsync(
                    [("i", details.ExternalId), ("Season", season.ToString(CultureInfo.InvariantCulture))],
                    cancellationToken);
                if (body is null)
                {
                    continue;
                }

                await _cache.SetAsync(cacheName, key, body, cancellationToken);
            }

            titles.AddRange(OmdbParser.ParseSeason(body, season));
        }

        return titles;
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Una solicitud a OMDb. Devuelve el cuerpo, o null si OMDb dice que no hay resultados ("not found", "Too many
    /// results"). El límite diario pausa la compuerta hasta mañana y lanza <see cref="ProviderQuotaExceededException"/>.
    /// </summary>
    private async Task<string?> GetAsync(IEnumerable<(string Name, string Value)> parameters, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new ProviderNotConfiguredException(Name);
        }

        await _gate.WaitAsync(cancellationToken);

        // La clave va en la dirección, así que este cliente no tiene registros de solicitudes (ver DependencyInjection).
        var url = _options.Omdb.BaseUrl.TrimEnd('/') + "/?apikey=" + Uri.EscapeDataString(_options.Omdb.ApiKey!.Trim()) +
            string.Concat(parameters.Select(p => $"&{p.Name}={Uri.EscapeDataString(p.Value)}"));

        string body;
        try
        {
            using var client = _http.CreateClient(HttpClientNames.Omdb);
            using var response = await client.GetAsync(url, cancellationToken);
            if ((int)response.StatusCode == 401)
            {
                // OMDb responde 401 a una clave inválida.
                body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Interpret(body, status401: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MetadataProviderException($"OMDb respondió con un error ({(int)response.StatusCode}).");
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MetadataProviderException("No se pudo conectar con OMDb. Revisa la conexión a internet.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MetadataProviderException("OMDb tardó demasiado en responder.", ex);
        }

        return Interpret(body, status401: false);
    }

    private string? Interpret(string body, bool status401)
    {
        string? error;
        try
        {
            error = OmdbParser.GetError(body);
        }
        catch (JsonException ex)
        {
            if (status401)
            {
                error = "Invalid API key!";
            }
            else
            {
                throw new MetadataProviderException("OMDb respondió algo que no se pudo leer.", ex);
            }
        }

        if (error is null)
        {
            return body;
        }

        if (OmdbParser.IsDailyLimit(error))
        {
            // Hasta el día siguiente en UTC (OMDb reinicia su contador a esa hora, más o menos): un minuto de margen.
            var tomorrow = new DateTimeOffset(_time.GetUtcNow().UtcDateTime.Date.AddDays(1), TimeSpan.Zero).AddMinutes(1);
            _gate.PauseUntil(tomorrow);
            _logger.LogWarning("OMDb llegó a su límite diario; la cola se pausa hasta {Resume:u}.", tomorrow);
            throw new ProviderQuotaExceededException(tomorrow);
        }

        if (status401 || OmdbParser.IsInvalidKey(error))
        {
            // Con una clave inválida, todos los elementos fallarían: se pausa y quedan pendientes.
            _gate.PauseFor(TimeSpan.FromMinutes(10));
            throw new ProviderNotConfiguredException(
                Name, "OMDb rechazó la clave. Revisa OMDB_API_KEY y que hayas activado la clave con el correo de OMDb.");
        }

        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Too many results", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        throw new MetadataProviderException($"OMDb respondió con un error: {error}");
    }
}
