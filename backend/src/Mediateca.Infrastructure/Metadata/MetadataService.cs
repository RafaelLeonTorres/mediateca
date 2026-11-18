using System.Globalization;
using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Metadata;
using Mediateca.Core.Naming;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Metadata;

/// <summary>Lo que se pidió no tiene arreglo reintentando: un identificador que no existe, o de otro proveedor. El mensaje es para el administrador.</summary>
public sealed class MetadataInputException(string message) : Exception(message);

/// <summary>Resultado de buscar los metadatos de un elemento.</summary>
public sealed record MetadataOutcome(MetadataStatus Status, string? Message, string? Title);

/// <summary>
/// Busca, elige y aplica los metadatos de una serie o película de primer nivel (PLAN.md, sección 8).
/// <list type="bullet">
/// <item><see cref="MatchAsync"/>: con el id de la etiqueta de la carpeta o, si no hay, buscando por el nombre de la
/// carpeta y eligiendo con <see cref="CandidateScorer"/>. Lo usan la cola del worker y "Actualizar metadatos".</item>
/// <item><see cref="IdentifyAsync"/>: el administrador eligió el resultado (o pegó el id): se aplica sin puntuar.</item>
/// </list>
/// Las temporadas y los episodios heredan: quedan en Matched, y los episodios reciben su título si el proveedor lo tiene.
/// Nada se cambia hasta tener todo lo que hace falta del proveedor, así que un límite de solicitudes a la mitad deja
/// el elemento como estaba (y en la cola).
/// </summary>
public sealed class MetadataService(
    MediatecaDbContext db,
    IEnumerable<IMetadataProvider> providers,
    ImageDownloader images,
    IOptions<StorageOptions> storage,
    TimeProvider time,
    ILogger<MetadataService> logger)
{
    private const int MaxTitleLength = 500;

    private const int MaxContentRatingLength = 20;

    // Dos consultas más (los nombres entre paréntesis) cuando la primera no da un resultado seguro.
    private const int MaxQueries = 3;

    public IMetadataProvider? ProviderFor(LibraryType type) =>
        MetadataProviders.For(type) is { } name ? providers.FirstOrDefault(p => p.Name == name) : null;

    // ---------------------------------------------------------------- automático

    /// <summary>
    /// Pone los metadatos de un elemento. Devuelve null si no existe o no es de primer nivel. Si el proveedor no
    /// conoce el elemento queda en NotFound (salvo en una actualización de uno que ya estaba encontrado: se queda
    /// como está). Los fallos del proveedor se propagan como <see cref="MetadataProviderException"/>.
    /// </summary>
    public async Task<MetadataOutcome?> MatchAsync(Guid itemId, bool refresh, CancellationToken cancellationToken)
    {
        var item = await LoadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var provider = RequireProvider(item);
        var knownId = KnownId(item, provider);

        ProviderMetadata? details;
        string? notFoundMessage = null;
        if (knownId is not null)
        {
            details = await provider.GetDetailsAsync(knownId, refresh, cancellationToken);
            notFoundMessage = $"{MetadataProviders.DisplayName(provider.Name)} no tiene el id {knownId}.";
        }
        else
        {
            var source = await LoadSourceAsync(item, cancellationToken);
            var best = await FindAsync(item, provider, source, refresh, cancellationToken);
            details = best is null ? null : await provider.GetDetailsAsync(best.Candidate, refresh, cancellationToken);
            notFoundMessage =
                $"No se encontró en {MetadataProviders.DisplayName(provider.Name)} una coincidencia segura para «{source.DisplayName}». " +
                "Usa Identificar para elegirla a mano.";
        }

        if (details is null)
        {
            if (item.MetadataStatus != MetadataStatus.Matched)
            {
                item.MetadataStatus = MetadataStatus.NotFound;
                item.MetadataMessage = Truncate(notFoundMessage, 500);
                await db.SaveChangesAsync(cancellationToken);
            }

            return new MetadataOutcome(MetadataStatus.NotFound, notFoundMessage, item.Title);
        }

        await ApplyAsync(item, details, replaceImages: false, refresh, cancellationToken);
        return new MetadataOutcome(MetadataStatus.Matched, null, item.Title);
    }

    /// <summary>Deja el elemento en Error con el mensaje (los fallos que no son del límite de solicitudes).</summary>
    public async Task MarkErrorAsync(Guid itemId, string message, CancellationToken cancellationToken)
    {
        var text = Truncate(message, 500);
        await db.MediaItems
            .Where(item => item.Id == itemId && item.MetadataStatus != MetadataStatus.Matched)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.MetadataStatus, MetadataStatus.Error)
                    .SetProperty(item => item.MetadataMessage, text),
                cancellationToken);
    }

    // ---------------------------------------------------------------- identificar

    /// <summary>
    /// Aplica a mano el resultado que el administrador eligió: un id o una dirección pegada (<c>anilist.co/anime/21</c>,
    /// <c>tt0118799</c>, una dirección de IMDb) o el id de un resultado de la búsqueda. Se pide al proveedor en el
    /// momento (el administrador está esperando la respuesta) y también se descargan las imágenes del nuevo resultado.
    /// </summary>
    public async Task<MetadataOutcome?> IdentifyAsync(Guid itemId, string input, CancellationToken cancellationToken)
    {
        var item = await LoadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var provider = RequireProvider(item);
        var parsed = IdentifierParser.Parse(input) ?? throw new MetadataInputException(
            "No reconozco ese identificador. Pega el id o la dirección de AniList (anilist.co/anime/21) o de IMDb (tt0118799).");
        if (parsed.Provider != provider.Name)
        {
            throw new MetadataInputException(
                $"Esta biblioteca usa {MetadataProviders.DisplayName(provider.Name)}, pero el identificador es de " +
                $"{MetadataProviders.DisplayName(parsed.Provider)}.");
        }

        var details = await provider.GetDetailsAsync(parsed.Id, refresh: false, cancellationToken)
            ?? throw new MetadataInputException(
                $"{MetadataProviders.DisplayName(provider.Name)} no tiene el identificador {parsed.Id}.");

        await ApplyAsync(item, details, replaceImages: true, refresh: false, cancellationToken);
        return new MetadataOutcome(MetadataStatus.Matched, null, item.Title);
    }

    /// <summary>
    /// Busca en el proveedor de la biblioteca del elemento para el diálogo Identificar. Sin texto usa el título
    /// del elemento. Los resultados van ordenados del que más se parece al que menos.
    /// </summary>
    public async Task<IReadOnlyList<SearchCandidate>?> SearchAsync(
        Guid itemId, string? text, int? year, CancellationToken cancellationToken)
    {
        var item = await LoadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var provider = RequireProvider(item);
        var query = string.IsNullOrWhiteSpace(text) ? item.Title : text.Trim();
        var kind = KindOf(item);
        var found = await provider.SearchAsync(new SearchQuery(query, year, kind), refresh: false, cancellationToken);

        var hint = SearchNameCleaner.Clean(query);
        if (year is not null)
        {
            hint = hint with { Year = year };
        }

        return CandidateScorer.Rank(hint, found, new ScoringOptions(UseFormat: provider.Name == MetadataProviders.AniList))
            .Select(scored => scored.Candidate)
            .ToList();
    }

    // ---------------------------------------------------------------- búsqueda por nombre

    private async Task<ScoredCandidate?> FindAsync(
        MediaItem item, IMetadataProvider provider, ItemSource source, bool refresh, CancellationToken cancellationToken)
    {
        var isAnime = provider.Name == MetadataProviders.AniList;
        NameHint hint;
        List<string> queries;
        ScoringOptions options;

        if (isAnime)
        {
            hint = SearchNameCleaner.Clean(source.RawName);
            hint = hint with { IsMovie = hint.IsMovie || source.HasLatAndSub };
            queries = new[] { hint.Title }.Concat(hint.Alternates).Where(q => q.Length > 0).Distinct().Take(MaxQueries).ToList();
            options = new ScoringOptions(UseFormat: true);
        }
        else
        {
            // OMDb: el título que ya sacó el escáner (sin año ni etiquetas), con el año aparte.
            var cleaned = SearchNameCleaner.Clean(item.Title);
            hint = cleaned with { Year = item.Year ?? cleaned.Year, IsMovie = false };
            queries = [item.Title];
            options = new ScoringOptions(UseFormat: false);
        }

        var candidates = new List<SearchCandidate>();
        ScoredCandidate? best = null;
        var kind = KindOf(item);
        var year = isAnime ? null : item.Year ?? hint.Year;

        foreach (var query in queries)
        {
            var found = await provider.SearchAsync(new SearchQuery(query, year, kind), refresh, cancellationToken);
            if (found.Count == 0 && year is not null && !isAnime)
            {
                // Con el año de la carpeta mal puesto, OMDb no encuentra nada: se vuelve a buscar sin él.
                found = await provider.SearchAsync(new SearchQuery(query, null, kind), refresh, cancellationToken);
            }

            foreach (var candidate in found)
            {
                if (candidates.All(existing => existing.ExternalId != candidate.ExternalId))
                {
                    candidates.Add(candidate);
                }
            }

            best = CandidateScorer.PickBest(hint, candidates, options);
            if (best is not null && best.Similarity >= CandidateScorer.ConfidentSimilarity)
            {
                break;
            }
        }

        return best;
    }

    private static MetadataSearchKind KindOf(MediaItem item) => item switch
    {
        Movie => MetadataSearchKind.Movie,
        Series when item.Library?.Type == LibraryType.Series => MetadataSearchKind.Series,
        _ => MetadataSearchKind.Any,
    };

    private static string? KnownId(MediaItem item, IMetadataProvider provider) =>
        provider.Name == MetadataProviders.AniList
            ? item.AniListId?.ToString(CultureInfo.InvariantCulture)
            : item.ImdbId;

    // ---------------------------------------------------------------- aplicar

    private async Task ApplyAsync(
        MediaItem item, ProviderMetadata details, bool replaceImages, bool refresh, CancellationToken cancellationToken)
    {
        var provider = RequireProvider(item);

        // 1. Todo lo que hace falta del proveedor, antes de cambiar nada.
        var seasons = new List<Season>();
        var episodes = new List<Episode>();
        if (item is Series)
        {
            seasons = await db.MediaItems.OfType<Season>().Where(s => s.ParentId == item.Id).ToListAsync(cancellationToken);
            var seasonIds = seasons.Select(s => s.Id).ToList();
            episodes = await db.MediaItems.OfType<Episode>()
                .Where(e => e.ParentId == item.Id || (e.ParentId != null && seasonIds.Contains(e.ParentId.Value)))
                .ToListAsync(cancellationToken);
        }

        var titles = await LoadEpisodeTitlesAsync(provider, details, episodes, refresh, cancellationToken);

        // 2. Imágenes (los fallos de descarga no impiden aplicar el resto).
        var poster = await ChooseImageAsync(item.PosterPath, details.PosterUrl, item.Id, "poster", replaceImages, cancellationToken);
        var backdrop = await ChooseImageAsync(item.BackdropPath, details.BackdropUrl, item.Id, "backdrop", replaceImages, cancellationToken);

        // 3. Los campos.
        if (item is Series && item.ScanKey is null)
        {
            // El escáner reconoce la serie por el título y el año de su carpeta, que están a punto de cambiar.
            item.ScanKey = Truncate(ScanKeys.Series(item.Title, item.Year), 600);
        }

        item.Title = Truncate(details.Title, MaxTitleLength);
        item.SortTitle = SortTitles.From(item.Title);
        item.OriginalTitle = details.OriginalTitle is null ? null : Truncate(details.OriginalTitle, MaxTitleLength);
        item.Year = details.Year ?? item.Year;
        item.EndYear = details.EndYear;
        item.Status = details.Status;
        item.Overview = details.Overview ?? item.Overview;
        item.ContentRating = details.ContentRating is null ? item.ContentRating : Truncate(details.ContentRating, MaxContentRatingLength);
        item.CommunityRating = details.CommunityRating ?? item.CommunityRating;
        item.RuntimeMinutes = details.RuntimeMinutes ?? item.RuntimeMinutes;
        item.Genres = details.Genres.Count > 0 ? details.Genres.ToList() : item.Genres;
        item.Tags = details.Tags.ToList();
        item.Studios = details.Studios.Count > 0 ? details.Studios.ToList() : item.Studios;
        item.Directors = details.Directors.Count > 0 ? details.Directors.ToList() : item.Directors;
        item.Actors = details.Actors.Count > 0 ? details.Actors.ToList() : item.Actors;
        item.ExternalLinksJson = ExternalLinks.Serialize(details.Links.ToList());
        item.NextAiringAt = details.NextAiringAt;
        item.NextAiringEpisode = details.NextAiringEpisode;
        item.ImdbId = details.ImdbId ?? item.ImdbId;
        item.AniListId = details.AniListId ?? item.AniListId;
        var previousPoster = item.PosterPath;
        item.PosterPath = poster;
        item.BackdropPath = backdrop;
        item.MetadataUpdatedAt = time.GetUtcNow();
        item.MetadataStatus = MetadataStatus.Matched;
        item.MetadataMessage = null;

        // 4. Temporadas y episodios: heredan el estado; los episodios, su título si el proveedor lo tiene.
        foreach (var season in seasons)
        {
            season.MetadataStatus = MetadataStatus.Matched;
            // La temporada que usaba la imagen de la serie sigue usándola (aunque cambie de archivo).
            if (season.PosterPath is null || season.PosterPath == previousPoster)
            {
                season.PosterPath = poster;
            }
        }

        foreach (var episode in episodes)
        {
            episode.MetadataStatus = MetadataStatus.Matched;
            if (FindTitle(titles, episode) is { } title)
            {
                episode.Title = Truncate(title, MaxTitleLength);
                episode.SortTitle = SortTitles.From(episode.Title);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Metadatos de «{Title}» ({Provider} {ExternalId}) aplicados; {Titled} episodios con título.",
            item.Title, details.Provider, details.ExternalId, titles.Count);
    }

    private async Task<TitleLookup> LoadEpisodeTitlesAsync(
        IMetadataProvider provider,
        ProviderMetadata details,
        List<Episode> episodes,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var all = new List<EpisodeTitle>(details.EpisodeTitles);
        if (episodes.Count > 0)
        {
            var seasons = episodes
                .Where(e => e.SeasonNumber is > 0)
                .Select(e => e.SeasonNumber!.Value)
                .Distinct()
                .Order()
                .ToList();

            if (refresh && seasons.Count > 1 && provider.Name == MetadataProviders.Omdb)
            {
                // Al actualizar, solo la última temporada se vuelve a pedir; las demás salen de la caché.
                var last = seasons[^1];
                all.AddRange(await provider.GetEpisodeTitlesAsync(details, [last], true, cancellationToken));
                all.AddRange(await provider.GetEpisodeTitlesAsync(details, seasons.Where(s => s != last).ToList(), false, cancellationToken));
            }
            else
            {
                all.AddRange(await provider.GetEpisodeTitlesAsync(details, seasons, refresh, cancellationToken));
            }
        }

        return new TitleLookup(all);
    }

    private static string? FindTitle(TitleLookup titles, Episode episode)
    {
        // Los especiales (temporada 0) no están numerados igual en ningún proveedor.
        if (episode.SeasonNumber == 0)
        {
            return null;
        }

        var number = episode.EpisodeNumber ?? episode.AbsoluteNumber;
        if (number is null)
        {
            return null;
        }

        if (episode.SeasonNumber is { } season && titles.BySeason.TryGetValue((season, number.Value), out var seasonal))
        {
            return seasonal;
        }

        return titles.ByNumber.TryGetValue(number.Value, out var plain) ? plain : null;
    }

    private async Task<string?> ChooseImageAsync(
        string? current, string? url, Guid itemId, string kind, bool replace, CancellationToken cancellationToken)
    {
        // Las imágenes junto a los medios (poster.jpg, backdrop.jpg…) mandan sobre las del proveedor.
        if (current is not null && storage.Value.IsInsideMediaRoots(current) && File.Exists(current))
        {
            return current;
        }

        if (url is null)
        {
            return current;
        }

        var downloaded = current is not null && !replace && File.Exists(current);
        if (downloaded)
        {
            return current;
        }

        return await images.DownloadAsync(url, itemId, kind, cancellationToken) ?? current;
    }

    // ---------------------------------------------------------------- lectura

    private async Task<MediaItem?> LoadAsync(Guid itemId, CancellationToken cancellationToken) =>
        await db.MediaItems
            .Include(item => item.Library)
            .Where(item => item.Id == itemId && (item is Series || item is Movie))
            .FirstOrDefaultAsync(cancellationToken);

    private IMetadataProvider RequireProvider(MediaItem item)
    {
        var type = item.Library?.Type ?? throw new InvalidOperationException("El elemento no tiene biblioteca.");
        var provider = ProviderFor(type) ?? throw new MetadataInputException("Esta biblioteca no tiene proveedor de metadatos.");
        if (!provider.IsConfigured)
        {
            throw new ProviderNotConfiguredException(provider.Name);
        }

        // En pausa (límite de solicitudes): fallar enseguida en lugar de dejar la solicitud esperando horas.
        if (provider.PausedUntil is { } until)
        {
            throw new ProviderRateLimitedException(provider.Name, until - time.GetUtcNow());
        }

        return provider;
    }

    /// <summary>
    /// El nombre de la carpeta tal como está en el disco (el título de la serie ya no lo conserva entero: se pierde lo
    /// que va detrás de un año) y si es una película en una carpeta de anime (archivos Lat y Sub).
    /// </summary>
    private async Task<ItemSource> LoadSourceAsync(MediaItem item, CancellationToken cancellationToken)
    {
        if (item is Movie movie)
        {
            return new ItemSource(item.Title, item.Title, false, movie.FilePath);
        }

        var seasonIds = await db.MediaItems.OfType<Season>()
            .Where(s => s.ParentId == item.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        var files = await db.MediaItems.AsNoTracking().OfType<Episode>()
            .Where(e => e.ParentId == item.Id || (e.ParentId != null && seasonIds.Contains(e.ParentId.Value)))
            .Select(e => e.FilePath)
            .ToListAsync(cancellationToken);

        var stems = files
            .Select(path => Path.GetFileNameWithoutExtension(path).Trim().ToLowerInvariant())
            .ToHashSet();
        var hasLatAndSub = stems.Contains("lat") && stems.Contains("sub");

        var raw = FolderName(item.Library!, files.OrderBy(path => path, StringComparer.Ordinal).FirstOrDefault()) ?? item.Title;
        return new ItemSource(raw, raw, hasLatAndSub, files.FirstOrDefault());
    }

    // La primera carpeta después de la carpeta de la biblioteca; nulo si el archivo está suelto en ella.
    private static string? FolderName(Library library, string? filePath)
    {
        if (filePath is null)
        {
            return null;
        }

        var root = library.Paths.FirstOrDefault(path => MediaPaths.IsWithin(filePath, path));
        if (root is null)
        {
            return null;
        }

        var segments = Path.GetRelativePath(root, filePath)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? segments[0] : null;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    private sealed record ItemSource(string RawName, string DisplayName, bool HasLatAndSub, string? SamplePath);

    private sealed class TitleLookup
    {
        public TitleLookup(IEnumerable<EpisodeTitle> titles)
        {
            foreach (var title in titles)
            {
                if (string.IsNullOrWhiteSpace(title.Title))
                {
                    continue;
                }

                if (title.Season is { } season)
                {
                    BySeason.TryAdd((season, title.Number), title.Title);
                }
                else
                {
                    ByNumber.TryAdd(title.Number, title.Title);
                }
            }
        }

        public Dictionary<(int Season, int Number), string> BySeason { get; } = new();

        public Dictionary<int, string> ByNumber { get; } = new();

        public int Count => BySeason.Count + ByNumber.Count;
    }
}
