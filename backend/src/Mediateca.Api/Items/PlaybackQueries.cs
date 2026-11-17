using Mediateca.Core.Media;
using Mediateca.Core.Users;
using Mediateca.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mediateca.Api.Items;

/// <summary>
/// Consultas de la reproducción: el contexto de un episodio (serie y carpeta), sus vecinos dentro de la
/// misma carpeta y el "A continuación" de una serie o carpeta. Todas parten de los elementos visibles
/// que les pasa el endpoint; ninguna filtra por su cuenta.
/// </summary>
internal static class PlaybackQueries
{
    private const int MaxAncestors = 4;

    /// <summary>Los ancestros de varios elementos, cargados por niveles (una consulta por nivel, no una por elemento).</summary>
    public sealed class AncestorLookup(IReadOnlyDictionary<Guid, MediaItem> known)
    {
        /// <summary>Del padre inmediato hacia arriba: la temporada o carpeta primero y la serie después.</summary>
        public IReadOnlyList<MediaItem> ChainOf(MediaItem item)
        {
            var chain = new List<MediaItem>();
            var parentId = item.ParentId;
            while (parentId is { } id && chain.Count < MaxAncestors && known.TryGetValue(id, out var parent))
            {
                chain.Add(parent);
                parentId = parent.ParentId;
            }

            return chain;
        }
    }

    public static async Task<AncestorLookup> LoadAncestorsAsync(
        MediatecaDbContext db, IReadOnlyCollection<MediaItem> items, CancellationToken cancellationToken)
    {
        var known = new Dictionary<Guid, MediaItem>();
        var pending = items
            .Where(item => item.ParentId != null)
            .Select(item => item.ParentId!.Value)
            .Distinct()
            .ToList();

        for (var level = 0; level < MaxAncestors && pending.Count > 0; level++)
        {
            var batch = await db.MediaItems.AsNoTracking()
                .Where(item => pending.Contains(item.Id))
                .ToListAsync(cancellationToken);
            foreach (var parent in batch)
            {
                known[parent.Id] = parent;
            }

            pending = batch
                .Where(parent => parent.ParentId != null && !known.ContainsKey(parent.ParentId.Value))
                .Select(parent => parent.ParentId!.Value)
                .Distinct()
                .ToList();
        }

        return new AncestorLookup(known);
    }

    /// <summary>Título de la serie de un episodio (null si no está en una serie).</summary>
    public static string? SeriesTitleOf(IReadOnlyList<MediaItem> chain) =>
        chain.OfType<Series>().FirstOrDefault()?.Title;

    /// <summary>Título de la temporada o carpeta de un episodio, o del álbum de una pista (null si cuelga directo de la serie).</summary>
    public static string? ParentTitleOf(IReadOnlyList<MediaItem> chain)
    {
        if (chain.Count == 0)
        {
            return null;
        }

        var parent = chain[0];
        return parent is Season || parent is MusicAlbum ? parent.Title : null;
    }

    /// <summary>
    /// Un episodio con número o una pista pueden tener anterior y siguiente. Los archivos sin número
    /// ("Lat.mp4" y "Sub.mp4" de una película) son versiones del mismo contenido, no una secuencia.
    /// </summary>
    public static bool IsSequential(MediaItem item) => item switch
    {
        Episode episode => (episode.EpisodeNumber ?? episode.AbsoluteNumber) != null,
        Track => true,
        _ => false,
    };

    /// <summary>
    /// El anterior y el siguiente dentro del mismo padre, en el orden de <see cref="ItemMapping.OrderChildren"/>.
    /// Nunca salta de una carpeta a otra: si viste el 3 de "Lat", sigue el 4 de "Lat".
    /// </summary>
    public static async Task<(MediaItem? Previous, MediaItem? Next)> NeighborsAsync(
        IQueryable<MediaItem> visible, PlayableItem item, CancellationToken cancellationToken)
    {
        if (item.ParentId is not { } parentId || !IsSequential(item))
        {
            return (null, null);
        }

        var siblings = await visible.AsNoTracking()
            .OfType<PlayableItem>()
            .Where(sibling => sibling.ParentId == parentId)
            .ToListAsync(cancellationToken);
        var ordered = ItemMapping.OrderChildren(siblings.Where(IsSequential));
        var index = ordered.FindIndex(sibling => sibling.Id == item.Id);
        if (index < 0)
        {
            return (null, null);
        }

        return (index > 0 ? ordered[index - 1] : null, index < ordered.Count - 1 ? ordered[index + 1] : null);
    }

    /// <summary>"Continuar viendo" y "A continuación": arma las tarjetas con el título de la serie y de la carpeta.</summary>
    public static async Task<List<ResumeItem>> BuildResumeItemsAsync(
        MediatecaDbContext db,
        IReadOnlyList<(PlayableItem Item, UserItemData? Data)> entries,
        CancellationToken cancellationToken)
    {
        var lookup = await LoadAncestorsAsync(db, entries.Select(entry => (MediaItem)entry.Item).ToList(), cancellationToken);
        return entries.Select(entry =>
        {
            var chain = lookup.ChainOf(entry.Item);
            // La miniatura propia; si no tiene, la de su temporada o la de su serie.
            var imageId = !string.IsNullOrEmpty(entry.Item.PosterPath)
                ? entry.Item.Id
                : chain.FirstOrDefault(ancestor => !string.IsNullOrEmpty(ancestor.PosterPath))?.Id;
            return new ResumeItem(
                entry.Item.Id,
                ItemMapping.KindOf(entry.Item),
                entry.Item.Title,
                SeriesTitleOf(chain),
                ParentTitleOf(chain),
                imageId,
                entry.Item.DurationSeconds,
                entry.Data?.PositionSeconds ?? 0,
                entry.Data?.LastPlayedAt);
        }).ToList();
    }

    /// <summary>
    /// "A continuación" de una serie o de una temporada o carpeta (PLAN.md, sección 9). Null si no
    /// queda nada por ver.
    /// </summary>
    public static async Task<ResumeItem?> FindNextUpAsync(
        MediatecaDbContext db, Guid userId, IQueryable<MediaItem> visible, MediaItem container, CancellationToken cancellationToken)
    {
        var containerId = container.Id;
        var episodesQuery = container is Series
            ? visible.OfType<Episode>().Where(e => e.ParentId == containerId || e.Parent!.ParentId == containerId)
            : visible.OfType<Episode>().Where(e => e.ParentId == containerId);
        var episodes = await episodesQuery.AsNoTracking().ToListAsync(cancellationToken);
        if (episodes.Count == 0)
        {
            return null;
        }

        var episodeIds = episodes.Select(e => e.Id).ToList();
        var progress = (await db.ItemProgress.AsNoTracking()
                .Where(d => d.UserId == userId && episodeIds.Contains(d.ItemId))
                .ToListAsync(cancellationToken))
            .ToDictionary(d => d.ItemId);

        // Las carpetas (con su número de temporada, si tienen) en el orden de la lista de hijos.
        var folders = new List<(Guid Id, int? SeasonNumber)>();
        if (container is Season season)
        {
            folders.Add((season.Id, season.SeasonNumber));
        }
        else
        {
            var seasons = await visible.AsNoTracking().OfType<Season>()
                .Where(s => s.ParentId == containerId)
                .ToListAsync(cancellationToken);
            folders.AddRange(ItemMapping.OrderChildren(seasons).OfType<Season>().Select(s => (s.Id, s.SeasonNumber)));
            if (episodes.Any(e => e.ParentId == containerId))
            {
                // Episodios sueltos, directo en la serie.
                folders.Add((containerId, null));
            }
        }

        folders = folders.Where(folder => episodes.Any(e => e.ParentId == folder.Id)).ToList();
        if (folders.Count == 0)
        {
            return null;
        }

        // Dónde empezar: la carpeta del último episodio reproducido o, si no hay, la primera
        // (sin contar los especiales cuando hay otras temporadas).
        var lastEpisode = LastReproduced(episodes, progress);
        var start = lastEpisode is not null
            ? folders.FindIndex(folder => folder.Id == lastEpisode.ParentId)
            : Math.Max(0, folders.FindIndex(folder => folder.SeasonNumber != 0));
        if (start < 0)
        {
            start = 0;
        }

        for (var index = start; index < folders.Count; index++)
        {
            // Solo las temporadas con número siguen con la siguiente. Las carpetas "Lat" y "Sub" no se mezclan.
            if (index > start && (folders[index].SeasonNumber is null || folders[index - 1].SeasonNumber is null))
            {
                break;
            }

            var folderId = folders[index].Id;
            var inFolder = ItemMapping.OrderChildren(episodes.Where(e => e.ParentId == folderId));
            if (PickInFolder(inFolder, progress) is PlayableItem pick)
            {
                var resume = await BuildResumeItemsAsync(db, [(pick, progress.GetValueOrDefault(pick.Id))], cancellationToken);
                return resume[0];
            }
        }

        return null;
    }

    /// <summary>Un elemento que el usuario ya reprodujo: lo vio o lo dejó a medias.</summary>
    private static bool IsReproduced(UserItemData? data) =>
        data is { LastPlayedAt: not null } && (data.Played || data.PositionSeconds > 0);

    /// <summary>El episodio reproducido más recientemente (null si no ha reproducido ninguno).</summary>
    private static Episode? LastReproduced(IReadOnlyList<Episode> episodes, IReadOnlyDictionary<Guid, UserItemData> progress) =>
        episodes
            .Where(e => IsReproduced(progress.GetValueOrDefault(e.Id)))
            .OrderByDescending(e => progress[e.Id].LastPlayedAt)
            .ThenBy(e => e.Id)
            .FirstOrDefault();

    /// <summary>
    /// El episodio que sigue dentro de una carpeta ya ordenada. Si el último reproducido quedó a medias,
    /// ese mismo; si ya se vio, el siguiente sin ver (los archivos sin número no cuentan: son versiones).
    /// Sin historial, el primero sin ver. Si después del último no queda nada, el primero sin ver de la
    /// carpeta. Null si todo está visto.
    /// </summary>
    private static MediaItem? PickInFolder(IReadOnlyList<MediaItem> ordered, IReadOnlyDictionary<Guid, UserItemData> progress)
    {
        bool IsPlayed(MediaItem item) => progress.TryGetValue(item.Id, out var data) && data.Played;

        var lastIndex = -1;
        DateTimeOffset? lastAt = null;
        for (var index = 0; index < ordered.Count; index++)
        {
            var data = progress.GetValueOrDefault(ordered[index].Id);
            if (IsReproduced(data) && (lastAt is null || data!.LastPlayedAt > lastAt))
            {
                lastIndex = index;
                lastAt = data!.LastPlayedAt;
            }
        }

        if (lastIndex >= 0)
        {
            var last = ordered[lastIndex];
            if (!IsPlayed(last))
            {
                return last;
            }

            if (!IsSequential(last))
            {
                return null;
            }

            for (var index = lastIndex + 1; index < ordered.Count; index++)
            {
                if (!IsPlayed(ordered[index]) && IsSequential(ordered[index]))
                {
                    return ordered[index];
                }
            }

            return ordered.FirstOrDefault(item => !IsPlayed(item) && IsSequential(item));
        }

        return ordered.FirstOrDefault(item => !IsPlayed(item));
    }
}
