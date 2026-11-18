using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Mediateca.Core.Libraries;
using Mediateca.Core.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>
/// AniList (GraphQL, sin clave): la fuente de los metadatos del anime (PLAN.md, sección 8). Todas las solicitudes
/// pasan por una compuerta compartida de 1 cada 2 s; ante un 429 (o un bloqueo con <c>Retry-After</c>) la pausa
/// llega hasta donde diga el proveedor y se lanza <see cref="ProviderRateLimitedException"/>: no se reintenta.
/// </summary>
public sealed class AniListProvider : IMetadataProvider, IDisposable
{
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _http;
    private readonly IMetadataCache _cache;
    private readonly MetadataOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AniListProvider> _logger;
    private readonly ProviderGate _gate;

    public AniListProvider(
        IHttpClientFactory http,
        IMetadataCache cache,
        IOptions<MetadataOptions> options,
        TimeProvider time,
        ILogger<AniListProvider> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _time = time;
        _logger = logger;
        _gate = new ProviderGate(_options.AniList.RequestInterval, time);
    }

    public string Name => MetadataProviders.AniList;

    public bool IsConfigured => true;

    public DateTimeOffset? PausedUntil => _gate.PausedUntil;

    public bool Supports(LibraryType type) => type == LibraryType.Anime;

    public async Task<IReadOnlyList<SearchCandidate>> SearchAsync(
        SearchQuery query, bool refresh, CancellationToken cancellationToken)
    {
        var text = query.Text.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var key = MetadataCacheStore.SearchKey(text.ToLowerInvariant());
        var cacheName = Name + "-search";
        if (!refresh && await _cache.GetAsync(cacheName, key, _options.CacheMaxAge, cancellationToken) is { } cached)
        {
            return AniListParser.ParseSearch(cached);
        }

        var body = await PostAsync(AniListQueries.Search, new { search = text }, cancellationToken);
        var media = body is null ? null : AniListParser.ExtractSearch(body);
        if (media is null)
        {
            // Sin resultados válidos: no se guarda, para que un fallo del proveedor no se quede en la caché.
            return [];
        }

        await _cache.SetAsync(cacheName, key, media, cancellationToken);
        return AniListParser.ParseSearch(media);
    }

    public async Task<ProviderMetadata?> GetDetailsAsync(string externalId, bool refresh, CancellationToken cancellationToken)
    {
        if (!int.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return null;
        }

        var key = id.ToString(CultureInfo.InvariantCulture);
        if (!refresh && await _cache.GetAsync(Name, key, _options.CacheMaxAge, cancellationToken) is { } cached)
        {
            return AniListParser.ParseMedia(cached);
        }

        var body = await PostAsync(AniListQueries.Details, new { id }, cancellationToken);
        var media = body is null ? null : AniListParser.ExtractMedia(body);
        if (media is null)
        {
            return null;
        }

        await _cache.SetAsync(Name, key, media, cancellationToken);
        return AniListParser.ParseMedia(media);
    }

    public async Task<ProviderMetadata?> GetDetailsAsync(SearchCandidate candidate, bool refresh, CancellationToken cancellationToken)
    {
        // La búsqueda ya trajo todos los campos: se guarda como detalle y se evita otra solicitud.
        if (candidate.RawPayload is { } raw)
        {
            await _cache.SetAsync(Name, candidate.ExternalId, raw, cancellationToken);
            return AniListParser.ParseMedia(raw);
        }

        return await GetDetailsAsync(candidate.ExternalId, refresh, cancellationToken);
    }

    public Task<IReadOnlyList<EpisodeTitle>> GetEpisodeTitlesAsync(
        ProviderMetadata details, IReadOnlyCollection<int> seasons, bool refresh, CancellationToken cancellationToken) =>
        Task.FromResult(details.EpisodeTitles);

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Una solicitud GraphQL. Devuelve el cuerpo, o null si AniList responde 404 (el id no existe). Un 429 pausa la
    /// compuerta y lanza <see cref="ProviderRateLimitedException"/>.
    /// </summary>
    private async Task<string?> PostAsync(string query, object variables, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        HttpResponseMessage response;
        try
        {
            using var client = _http.CreateClient(HttpClientNames.AniList);
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.AniList.Endpoint)
            {
                Content = JsonContent.Create(new { query, variables }),
            };
            request.Headers.Accept.ParseAdd("application/json");
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MetadataProviderException("No se pudo conectar con AniList. Revisa la conexión a internet.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MetadataProviderException("AniList tardó demasiado en responder.", ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;

            // 429: se pasó del límite. Un bloqueo también llega con Retry-After (la documentación habla de un
            // bloqueo de 1 minuto): se pausa la compuerta y la cola espera, sin reintentar.
            if (status == 429 || ((status is 403 or 503) && response.Headers.RetryAfter is not null))
            {
                var wait = RetryAfter.From(response, _time.GetUtcNow(), DefaultRetryAfter);
                _gate.PauseFor(wait);
                _logger.LogWarning("AniList pidió esperar {Wait} (HTTP {Status}).", wait, status);
                throw new ProviderRateLimitedException(Name, wait);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (status == 404)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MetadataProviderException($"AniList respondió con un error ({status}).");
            }

            // Los errores de GraphQL llegan en "errors"; sin datos, es un fallo (o un id que no existe).
            try
            {
                // "is { }" descarta el null y deja la tupla: un "var (a, b)" no deconstruye una tupla anulable.
                if (AniListParser.ExtractError(body) is { } error)
                {
                    var (errorStatus, message) = error;
                    if (errorStatus == 404)
                    {
                        return null;
                    }

                    if (errorStatus == 429)
                    {
                        var wait = RetryAfter.From(response, _time.GetUtcNow(), DefaultRetryAfter);
                        _gate.PauseFor(wait);
                        throw new ProviderRateLimitedException(Name, wait);
                    }

                    throw new MetadataProviderException($"AniList respondió con un error: {message}");
                }
            }
            catch (JsonException ex)
            {
                throw new MetadataProviderException("AniList respondió algo que no se pudo leer.", ex);
            }

            return body;
        }
    }
}
