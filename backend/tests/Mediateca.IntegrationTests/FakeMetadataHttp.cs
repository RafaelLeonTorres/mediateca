using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mediateca.IntegrationTests;

/// <summary>
/// Los servidores de metadatos de las pruebas: AniList, OMDb y las imágenes. Las pruebas no tienen red; cada
/// <c>HttpClient</c> con nombre de la aplicación usa uno de estos manejadores y responde con lo que la prueba guardó.
/// </summary>
public sealed class FakeMetadataHttp
{
    public FakeAniListHandler AniList { get; } = new();

    public FakeOmdbHandler Omdb { get; } = new();

    public FakeImagesHandler Images { get; } = new();
}

/// <summary>AniList (GraphQL, POST): búsquedas por texto y detalles por id.</summary>
public sealed class FakeAniListHandler : HttpMessageHandler
{
    private int _searchCount;
    private int _detailCount;

    /// <summary>Respuestas de búsqueda: el texto buscado (en minúsculas) y el arreglo JSON de <c>media</c>.</summary>
    public ConcurrentDictionary<string, string> Searches { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Detalles: el id y el JSON del <c>Media</c>.</summary>
    public ConcurrentDictionary<int, string> Media { get; } = new();

    /// <summary>Si no es null, responde esto a todo (por ejemplo, un 429).</summary>
    public Func<HttpResponseMessage>? Fail { get; set; }

    public int SearchCount => Volatile.Read(ref _searchCount);

    public int DetailCount => Volatile.Read(ref _detailCount);

    public int RequestCount => SearchCount + DetailCount;

    public ConcurrentQueue<string> SearchTerms { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var query = document.RootElement.GetProperty("query").GetString() ?? "";
        var variables = document.RootElement.GetProperty("variables");

        if (query.Contains("Page(", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _searchCount);
        }
        else
        {
            Interlocked.Increment(ref _detailCount);
        }

        if (Fail is { } fail)
        {
            return fail();
        }

        if (query.Contains("Page(", StringComparison.Ordinal))
        {
            var term = variables.GetProperty("search").GetString() ?? "";
            SearchTerms.Enqueue(term);
            var media = Searches.TryGetValue(term, out var found) ? found : "[]";
            // Concatenado: en un $$""" las llaves de cierre seguidas ("}}}}}") no se pueden escribir.
            return Json("""{"data":{"Page":{"media":""" + media + "}}}");
        }

        var id = variables.GetProperty("id").GetInt32();
        return Media.TryGetValue(id, out var detail)
            ? Json("""{"data":{"Media":""" + detail + "}}")
            : Json("""{"errors":[{"message":"Not Found.","status":404}],"data":{"Media":null}}""", HttpStatusCode.NotFound);
    }

    internal static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>OMDb (GET con la clave en la dirección): búsquedas, detalles y temporadas.</summary>
public sealed class FakeOmdbHandler : HttpMessageHandler
{
    private int _requestCount;

    /// <summary>Búsquedas: el título buscado (en minúsculas) y la respuesta JSON completa.</summary>
    public ConcurrentDictionary<string, string> Searches { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Detalles: el id de IMDb y la respuesta JSON.</summary>
    public ConcurrentDictionary<string, string> Titles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Temporadas: "tt11280740:1" y la respuesta JSON.</summary>
    public ConcurrentDictionary<string, string> Seasons { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Func<HttpResponseMessage>? Fail { get; set; }

    public int RequestCount => Volatile.Read(ref _requestCount);

    /// <summary>Los parámetros de cada solicitud (sin la clave), para comprobar qué se pidió.</summary>
    public ConcurrentQueue<Dictionary<string, string>> Requests { get; } = new();

    /// <summary>La clave que llegó en la última solicitud.</summary>
    public string? LastApiKey { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        var parameters = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var values = parameters.AllKeys.Where(key => key is not null).ToDictionary(key => key!, key => parameters[key]!);
        LastApiKey = values.GetValueOrDefault("apikey");
        values.Remove("apikey");
        Requests.Enqueue(values);

        if (Fail is { } fail)
        {
            return Task.FromResult(fail());
        }

        const string NotFound = """{"Response":"False","Error":"Movie not found!"}""";
        string json;
        if (values.TryGetValue("s", out var search))
        {
            json = Searches.GetValueOrDefault(search, NotFound);
        }
        else if (values.TryGetValue("i", out var id) && values.TryGetValue("Season", out var season))
        {
            json = Seasons.GetValueOrDefault($"{id}:{season}", NotFound);
        }
        else if (values.TryGetValue("i", out id))
        {
            json = Titles.GetValueOrDefault(id, NotFound);
        }
        else
        {
            json = """{"Response":"False","Error":"Incorrect IMDb ID."}""";
        }

        return Task.FromResult(FakeAniListHandler.Json(json));
    }
}

/// <summary>Servidor de imágenes: responde un JPEG mínimo para cualquier dirección y guarda cuáles se pidieron.</summary>
public sealed class FakeImagesHandler : HttpMessageHandler
{
    public ConcurrentQueue<string> Requests { get; } = new();

    /// <summary>Respuestas especiales por dirección (por ejemplo, algo que no es una imagen).</summary>
    public ConcurrentDictionary<string, byte[]> Content { get; } = new();

    /// <summary>El JPEG que se devuelve por omisión para una dirección: empieza con la firma de JPEG y termina con la dirección.</summary>
    public static byte[] JpegFor(string url) => [0xFF, 0xD8, 0xFF, 0xE0, .. Encoding.UTF8.GetBytes(url)];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Enqueue(url);
        var bytes = Content.TryGetValue(url, out var special) ? special : JpegFor(url);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new("image/jpeg");
        return Task.FromResult(response);
    }
}

/// <summary>Respuestas de ejemplo con el mismo formato que las de AniList y OMDb.</summary>
public static class MetadataSamples
{
    /// <summary>Un <c>Media</c> de AniList. El primer episodio con título se numera desde 1.</summary>
    public static string AniListMedia(
        int id,
        string romaji,
        string? english,
        int year,
        string status = "FINISHED",
        string format = "TV",
        int? nextEpisode = null,
        string? cover = null,
        string? banner = null,
        params string[] episodeTitles)
    {
        var streaming = new JsonArray();
        for (var index = 0; index < episodeTitles.Length; index++)
        {
            streaming.Add(new JsonObject
            {
                ["title"] = $"Episode {index + 1} - {episodeTitles[index]}",
                ["thumbnail"] = $"https://img.test/{id}/{index + 1}.jpg",
            });
        }

        var media = new JsonObject
        {
            ["id"] = id,
            ["idMal"] = id + 1000,
            ["siteUrl"] = $"https://anilist.co/anime/{id}",
            ["title"] = new JsonObject { ["romaji"] = romaji, ["english"] = english, ["native"] = null },
            ["synonyms"] = new JsonArray(),
            ["description"] = "Una historia larga.<br><br>Con <i>dos</i> párrafos.<br>(Source: Prueba)",
            ["startDate"] = new JsonObject { ["year"] = year, ["month"] = 1, ["day"] = 6 },
            ["endDate"] = new JsonObject { ["year"] = status == "FINISHED" ? year : null, ["month"] = null, ["day"] = null },
            ["status"] = status,
            ["seasonYear"] = year,
            ["format"] = format,
            ["episodes"] = 12,
            ["duration"] = 24,
            ["genres"] = new JsonArray("Fantasy", "Drama"),
            ["tags"] = new JsonArray(
                new JsonObject { ["name"] = "Elf", ["rank"] = 80, ["isMediaSpoiler"] = false, ["isGeneralSpoiler"] = false },
                new JsonObject { ["name"] = "Secreto", ["rank"] = 99, ["isMediaSpoiler"] = true, ["isGeneralSpoiler"] = false }),
            ["studios"] = new JsonObject
            {
                ["edges"] = new JsonArray(new JsonObject
                {
                    ["isMain"] = true,
                    ["node"] = new JsonObject { ["name"] = "MADHOUSE", ["isAnimationStudio"] = true },
                }),
            },
            ["averageScore"] = 84,
            ["isAdult"] = false,
            ["coverImage"] = new JsonObject
            {
                ["extraLarge"] = cover ?? $"https://s4.anilist.co/cover/{id}.jpg",
                ["large"] = $"https://s4.anilist.co/cover-small/{id}.jpg",
            },
            ["bannerImage"] = banner ?? $"https://s4.anilist.co/banner/{id}.jpg",
            ["nextAiringEpisode"] = nextEpisode is { } episode
                ? new JsonObject { ["airingAt"] = 1790000000, ["episode"] = episode }
                : null,
            ["externalLinks"] = new JsonArray(),
            ["streamingEpisodes"] = streaming,
        };
        return media.ToJsonString();
    }

    public static string AniListArray(params string[] media) => "[" + string.Join(",", media) + "]";

    public static string OmdbSearch(params (string Id, string Title, string Year, string Type)[] results) =>
        new JsonObject
        {
            ["Search"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject
            {
                ["Title"] = r.Title,
                ["Year"] = r.Year,
                ["imdbID"] = r.Id,
                ["Type"] = r.Type,
                ["Poster"] = $"https://m.media-amazon.com/images/M/{r.Id}-search.jpg",
            }).ToArray()),
            ["totalResults"] = results.Length.ToString(),
            ["Response"] = "True",
        }.ToJsonString();

    public static string OmdbTitle(
        string id, string title, string year, string type = "movie", string rated = "PG", string runtime = "10 min", string? totalSeasons = null)
    {
        var json = new JsonObject
        {
            ["Title"] = title,
            ["Year"] = year,
            ["Rated"] = rated,
            ["Runtime"] = runtime,
            ["Genre"] = "Animation, Comedy",
            ["Director"] = "Sacha Goedegebure",
            ["Actors"] = "Big Buck Bunny, Frank, Rinky",
            ["Plot"] = "Un conejo enorme se defiende de tres roedores.",
            ["Production"] = "N/A",
            ["Poster"] = $"https://m.media-amazon.com/images/M/{id}.jpg",
            ["imdbRating"] = "7.2",
            ["imdbID"] = id,
            ["Type"] = type,
            ["Response"] = "True",
        };
        if (totalSeasons is not null)
        {
            json["totalSeasons"] = totalSeasons;
        }

        return json.ToJsonString();
    }

    public static string OmdbSeason(string id, int season, params string[] titles) =>
        new JsonObject
        {
            ["Title"] = "Serie",
            ["Season"] = season.ToString(),
            ["Episodes"] = new JsonArray(titles.Select((title, index) => (JsonNode)new JsonObject
            {
                ["Title"] = title,
                ["Released"] = "2022-02-18",
                ["Episode"] = (index + 1).ToString(),
                ["imdbRating"] = "8.0",
                ["imdbID"] = $"{id}-{index}",
            }).ToArray()),
            ["Response"] = "True",
        }.ToJsonString();
}
