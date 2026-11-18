using Mediateca.Core.Libraries;
using Mediateca.Core.Media;
using Mediateca.Core.Naming;
using Mediateca.Core.Scanning;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mediateca.Infrastructure.Scanning;

/// <summary>
/// Escáner incremental de una biblioteca (PLAN.md, sección 7). Recorre sus carpetas y compara cada
/// archivo, por ruta, tamaño y fecha de modificación, con lo que hay en la base de datos: crea lo
/// nuevo, actualiza lo que cambió y elimina lo que ya no existe. Guarda en lotes y deja el avance en
/// el <see cref="ScanJob"/> para que Angular lo consulte.
/// </summary>
public sealed class LibraryScanner(
    MediatecaDbContext db,
    IAudioTagReader tagReader,
    IOptions<StorageOptions> storage,
    ILogger<LibraryScanner> logger)
{
    /// <summary>Cada cuántos archivos se guarda y se limpia el rastreador de cambios de EF.</summary>
    private const int BatchSize = 200;

    private const int DeleteChunkSize = 500;

    private const int MaxTitleLength = 500;

    private const int MaxArtistLength = 300;

    private const int MaxScanKeyLength = 600;

    private const int MaxDepth = 32;

    // Imágenes junto a los medios que se usan como póster o carátula, por orden de preferencia.
    private static readonly string[] PosterCandidates =
        new[] { "poster", "folder", "cover" }
            .SelectMany(name => new[] { ".jpg", ".jpeg", ".png" }.Select(extension => name + extension))
            .ToArray();

    // Imagen de fondo junto a los medios (como la guarda Jellyfin).
    private static readonly string[] BackdropCandidates =
        new[] { "backdrop", "fanart", "background" }
            .SelectMany(name => new[] { ".jpg", ".jpeg", ".png" }.Select(extension => name + extension))
            .ToArray();

    // Miniatura de un episodio junto al video, como la guarda Jellyfin: "01-thumb.jpg" para "01.mp4".
    private static readonly string[] ThumbnailSuffixes = ["-thumb.jpg", "-thumb.jpeg", "-thumb.png", "-thumb.webp"];

    /// <summary>
    /// Corre el escaneo de un <see cref="ScanJob"/> que el <see cref="ScanJobRunner"/> ya reclamó (estado
    /// Running y StartedAt puestos). Nunca lanza por un fallo del escaneo: lo deja en el <see cref="ScanJob"/>.
    /// </summary>
    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.ScanJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        try
        {
            var library = await db.Libraries.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == job.LibraryId, cancellationToken);
            if (library is null)
            {
                job.Status = ScanStatus.Failed;
                job.ErrorMessage = "La biblioteca ya no existe.";
            }
            else
            {
                var context = new ScanContext(library, job);
                await ScanAsync(context, cancellationToken);
                job.Status = ScanStatus.Completed;

                // Una carpeta que no se pudo leer no hace fallar el escaneo (no se borra nada de lo que había
                // en ella), pero el administrador tiene que enterarse: casi siempre es un disco desconectado.
                if (context.UnreadableRoots.Count > 0)
                {
                    job.ErrorMessage = UnreadableFoldersMessage(context.UnreadableRoots);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = ScanStatus.Failed;
            job.ErrorMessage = "El escaneo se interrumpió porque el worker se detuvo. Vuelve a escanear la biblioteca.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falló el escaneo {JobId}.", jobId);
            job.Status = ScanStatus.Failed;
            job.ErrorMessage = $"El escaneo falló: {ex.Message}";
        }

        job.FinishedAt = DateTimeOffset.UtcNow;
        await SaveJobAsync(job, CancellationToken.None);
    }

    private string UnreadableFoldersMessage(IReadOnlyList<string> roots)
    {
        var displayPaths = roots
            .Select(root => MediaPaths.ToDisplayPath(root, storage.Value.DrivesPath))
            .ToList();
        var subject = displayPaths.Count == 1
            ? $"No se pudo leer {displayPaths[0]}."
            : $"No se pudieron leer estas carpetas: {string.Join(", ", displayPaths)}.";
        return subject + " Revisa que el disco esté conectado; si es un disco nuevo, vuelve a correr setup.ps1.";
    }

    private async Task ScanAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var library = context.Library;
        var job = context.Job;

        var files = EnumerateFiles(context, cancellationToken);
        var existing = await LoadExistingFilesAsync(library.Id, cancellationToken);
        await LoadParentsAsync(context, cancellationToken);

        var found = files.ToDictionary(file => file.FullPath, MediaPaths.Comparer);
        var toAdd = files.Where(file => !existing.ContainsKey(file.FullPath)).ToList();
        var toUpdate = files
            .Where(file => existing.TryGetValue(file.FullPath, out var old) &&
                (old.FileSize != file.Size || old.FileModifiedAt.UtcTicks != file.ModifiedAt.UtcTicks))
            .ToList();

        // Si una carpeta de la biblioteca no se pudo leer (disco desmontado), no se borra nada de lo que
        // había dentro: no se sabe si los archivos siguen ahí.
        var toRemove = existing.Values
            .Where(old => !found.ContainsKey(old.FilePath) &&
                !context.UnreadableRoots.Any(root => MediaPaths.IsWithin(old.FilePath, root)))
            .ToList();

        // 1. Eliminar los archivos que ya no existen.
        foreach (var chunk in toRemove.Chunk(DeleteChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ids = chunk.Select(old => old.Id).ToList();
            await db.MediaItems.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
            job.Removed += ids.Count;
            await SaveJobAsync(job, cancellationToken);
        }

        // 2. Actualizar los que cambiaron.
        foreach (var chunk in toUpdate.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ids = chunk.Select(file => existing[file.FullPath].Id).ToList();
            var items = await db.MediaItems.OfType<PlayableItem>()
                .Where(item => ids.Contains(item.Id))
                .ToDictionaryAsync(item => item.FilePath, MediaPaths.Comparer, cancellationToken);

            foreach (var file in chunk)
            {
                if (!items.TryGetValue(file.FullPath, out var item))
                {
                    continue;
                }

                try
                {
                    UpdateItem(context, item, file);
                    job.Updated++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RecordError(context, file, ex);
                }
            }

            await FlushAsync(context, cancellationToken);
        }

        // 3. Reacomodar los episodios que no cambiaron pero que ahora se leen distinto (carpetas Lat y
        //    Sub, nombres como "01.mp4", miniaturas). Conservan su id, y con él su progreso.
        if (library.Type is LibraryType.Series or LibraryType.Anime)
        {
            var changedPaths = toUpdate.Select(file => file.FullPath).ToHashSet(MediaPaths.Comparer);
            var unchanged = files.Where(file => existing.ContainsKey(file.FullPath) && !changedPaths.Contains(file.FullPath));
            await RearrangeEpisodesAsync(context, unchanged, existing, cancellationToken);
        }

        // 4. Crear los nuevos, en lotes.
        foreach (var file in toAdd)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                AddItem(context, file);
                context.Pending++;
                job.Added++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordError(context, file, ex);
            }

            if (context.Pending >= BatchSize)
            {
                await FlushAsync(context, cancellationToken);
            }
        }

        await FlushAsync(context, cancellationToken);

        // 5. Las temporadas de escaneos anteriores no tenían imagen: ahora llevan la de su carpeta o la de la serie.
        foreach (var (seasonId, posterPath) in context.SeasonPosters)
        {
            await db.MediaItems
                .Where(item => item.Id == seasonId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PosterPath, posterPath), cancellationToken);
        }

        // 5b. Las imágenes locales mandan sobre las que descargó el proveedor: si una carpeta ya guardada ahora tiene
        //     poster.jpg o backdrop.jpg, se usan. Si no tiene, no se toca lo que haya (puede ser el del proveedor).
        await RefreshLocalImagesAsync(context, files, existing, cancellationToken);

        // 6. Quitar los padres que se quedaron sin hijos (después de crear, para no borrar uno que va a recibirlos).
        await DeleteEmptyParentsAsync(library.Id, cancellationToken);

        DateTimeOffset? scannedAt = DateTimeOffset.UtcNow;
        await db.Libraries
            .Where(l => l.Id == library.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.LastScannedAt, scannedAt), cancellationToken);

        logger.LogInformation(
            "Escaneo de '{Library}': {Added} nuevos, {Updated} actualizados, {Removed} eliminados, {Errors} errores.",
            library.Name, job.Added, job.Updated, job.Removed, job.Errors);
    }

    // ---------------------------------------------------------------- lectura del disco

    private List<ScannedFile> EnumerateFiles(ScanContext context, CancellationToken cancellationToken)
    {
        var files = new List<ScannedFile>();

        foreach (var root in context.Library.Paths)
        {
            if (!Directory.Exists(root))
            {
                logger.LogWarning("La carpeta {Path} de la biblioteca '{Library}' no existe o no se puede leer.", root, context.Library.Name);
                context.Job.Errors++;
                context.UnreadableRoots.Add(root);
                continue;
            }

            try
            {
                foreach (var file in WalkFiles(root, cancellationToken))
                {
                    if (!MediaFiles.BelongsTo(context.Library.Type, file.Name))
                    {
                        continue;
                    }

                    try
                    {
                        files.Add(new ScannedFile(
                            file.FullName,
                            Path.GetRelativePath(root, file.FullName),
                            file.Length,
                            ToUtcMilliseconds(file.LastWriteTimeUtc)));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Un enlace roto o un archivo que desapareció mientras se recorría: se omite.
                        logger.LogDebug(ex, "Se omitió {Path}.", file.FullName);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "No se pudo recorrer la carpeta {Path}.", root);
                context.Job.Errors++;
                context.UnreadableRoots.Add(root);
            }
        }

        // Orden estable: los archivos de una misma carpeta quedan juntos y sus padres se crean una vez.
        files.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));
        return files;
    }

    // Recorre las carpetas a mano para saltar las ocultas y las inaccesibles sin detener el escaneo.
    // Oculto es lo que empieza con punto o lo que el sistema de archivos marca como oculto o de sistema
    // (Windows no marca como ocultos los nombres con punto). Los enlaces a carpetas se siguen, con un
    // límite de profundidad por si apuntan en círculo.
    private IEnumerable<FileInfo> WalkFiles(string root, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        var pending = new Stack<(DirectoryInfo Directory, int Depth)>();
        pending.Push((new DirectoryInfo(root), 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();

            List<FileSystemInfo> entries;
            try
            {
                entries = directory.EnumerateFileSystemInfos("*", options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "No se pudo leer la carpeta {Path}.", directory.FullName);
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry.Name.StartsWith('.'))
                {
                    continue;
                }

                if (entry is DirectoryInfo subdirectory)
                {
                    if (depth < MaxDepth)
                    {
                        pending.Push((subdirectory, depth + 1));
                    }
                }
                else if (entry is FileInfo file)
                {
                    yield return file;
                }
            }
        }
    }

    // PostgreSQL guarda microsegundos y el sistema de archivos, 100 ns: se redondea hacia abajo a
    // milisegundos para que la fecha leída de la base de datos sea igual a la del archivo.
    private static DateTimeOffset ToUtcMilliseconds(DateTime utc) =>
        new(utc.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, TimeSpan.Zero);

    private async Task<Dictionary<string, ExistingFile>> LoadExistingFilesAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var rows = await db.MediaItems.AsNoTracking()
            .OfType<PlayableItem>()
            .Where(item => item.LibraryId == libraryId)
            .Select(item => new { item.Id, item.FilePath, item.FileSize, item.FileModifiedAt })
            .ToListAsync(cancellationToken);

        var existing = new Dictionary<string, ExistingFile>(MediaPaths.Comparer);
        foreach (var row in rows)
        {
            existing[row.FilePath] = new ExistingFile(row.Id, row.FilePath, row.FileSize, row.FileModifiedAt);
        }

        return existing;
    }

    // Series, temporadas y álbumes que ya existen, para encontrarlos por su clave en lugar de duplicarlos.
    private async Task LoadParentsAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var libraryId = context.Library.Id;

        var series = await db.MediaItems.AsNoTracking().OfType<Series>()
            .Where(item => item.LibraryId == libraryId)
            .Select(item => new { item.Id, item.Title, item.Year, item.ScanKey, item.PosterPath, item.BackdropPath })
            .ToListAsync(cancellationToken);
        foreach (var row in series)
        {
            // Con metadatos, Title y Year ya no son los de la carpeta: se usa la clave que guardó el escaneo.
            context.Series.TryAdd(row.ScanKey ?? ScanKeys.Series(row.Title, row.Year), row.Id);
            context.SeriesImages[row.Id] = new KnownImages(row.PosterPath, row.BackdropPath);
        }

        var seasons = await db.MediaItems.AsNoTracking().OfType<Season>()
            .Where(item => item.LibraryId == libraryId && item.ParentId != null)
            .Select(item => new { item.Id, item.ParentId, item.Title, item.PosterPath })
            .ToListAsync(cancellationToken);
        foreach (var row in seasons)
        {
            context.Seasons.TryAdd(SeasonKey(row.ParentId!.Value, row.Title), new KnownSeason(row.Id, row.PosterPath));
        }

        var albums = await db.MediaItems.AsNoTracking().OfType<MusicAlbum>()
            .Where(item => item.LibraryId == libraryId)
            .Select(item => new { item.Id, item.AlbumArtist, item.Title })
            .ToListAsync(cancellationToken);
        foreach (var row in albums)
        {
            context.Albums.TryAdd(AlbumKey(row.AlbumArtist, row.Title), row.Id);
        }
    }

    // ---------------------------------------------------------------- altas

    private void AddItem(ScanContext context, ScannedFile file)
    {
        switch (context.Library.Type)
        {
            case LibraryType.Movies:
                AddMovie(context, file);
                break;
            case LibraryType.Series or LibraryType.Anime:
                AddEpisode(context, file);
                break;
            case LibraryType.Music:
                AddTrack(context, file);
                break;
            default:
                throw new InvalidOperationException($"Tipo de biblioteca desconocido: {context.Library.Type}.");
        }
    }

    private void AddMovie(ScanContext context, ScannedFile file)
    {
        var parsed = MovieNameParser.Parse(file.RelativePath);
        var movie = new Movie
        {
            Id = Guid.CreateVersion7(),
            LibraryId = context.Library.Id,
            Title = Truncate(parsed.Title, MaxTitleLength),
            SortTitle = SortTitles.From(parsed.Title),
            Year = parsed.Year,
            ImdbId = parsed.ImdbId,
            AniListId = parsed.AniListId,
            FilePath = file.FullPath,
            FileSize = file.Size,
            FileModifiedAt = file.ModifiedAt,
            DurationSeconds = tagReader.ReadDuration(file.FullPath),
            // Una película suelta en la carpeta de la biblioteca no tiene póster propio: la imagen sería de todas.
            PosterPath = IsInSubfolder(file) ? FindPoster(context, Path.GetDirectoryName(file.FullPath)) : null,
            BackdropPath = IsInSubfolder(file) ? FindBackdrop(context, Path.GetDirectoryName(file.FullPath)) : null,
            DateAdded = DateTimeOffset.UtcNow,
            MetadataStatus = MetadataStatus.Pending,
        };
        db.MediaItems.Add(movie);
    }

    private void AddEpisode(ScanContext context, ScannedFile file)
    {
        var layout = DescribeEpisode(context, file);
        var episode = new Episode
        {
            Id = Guid.CreateVersion7(),
            LibraryId = context.Library.Id,
            ParentId = layout.ParentId,
            Title = layout.Title,
            SortTitle = SortTitles.From(layout.Title),
            FilePath = file.FullPath,
            FileSize = file.Size,
            FileModifiedAt = file.ModifiedAt,
            DurationSeconds = tagReader.ReadDuration(file.FullPath),
            SeasonNumber = layout.SeasonNumber,
            EpisodeNumber = layout.EpisodeNumber,
            AbsoluteNumber = layout.AbsoluteNumber,
            PosterPath = layout.PosterPath,
            DateAdded = DateTimeOffset.UtcNow,
            MetadataStatus = MetadataStatus.Pending,
        };
        db.MediaItems.Add(episode);
    }

    /// <summary>
    /// Dónde va un episodio y cómo se muestra, según su ruta: la serie, la temporada o la carpeta
    /// ("Lat", "Sub") que lo contiene, sus números, su título y su miniatura. Crea los padres que falten.
    /// </summary>
    private EpisodeLayout DescribeEpisode(ScanContext context, ScannedFile file)
    {
        var parsed = context.Library.Type == LibraryType.Series
            ? EpisodeNameParser.ParseSeries(file.RelativePath)
            : EpisodeNameParser.ParseAnime(file.RelativePath);

        var seriesId = GetOrCreateSeries(context, file, parsed);
        Guid parentId;
        if (parsed.Group is { } group)
        {
            parentId = GetOrCreateSeason(context, seriesId, group, parsed.SeasonNumber, file);
        }
        else if (parsed.SeasonNumber is { } seasonNumber)
        {
            parentId = GetOrCreateSeason(context, seriesId, SeasonTitle(seasonNumber), seasonNumber, file);
        }
        else
        {
            parentId = seriesId;
        }

        var number = parsed.EpisodeNumber ?? parsed.AbsoluteNumber;
        var title = parsed.EpisodeTitle ?? number switch
        {
            { } n when parsed.SeasonNumber == 0 => $"Especial {n}",
            { } n => $"Episodio {n}",
            _ => Path.GetFileNameWithoutExtension(file.FullPath),
        };

        return new EpisodeLayout(
            parentId,
            parsed.SeasonNumber,
            parsed.EpisodeNumber,
            parsed.AbsoluteNumber,
            Truncate(title, MaxTitleLength),
            FindThumbnail(context, file));
    }

    private Guid GetOrCreateSeries(ScanContext context, ScannedFile file, ParsedEpisode parsed)
    {
        var key = ScanKeys.Series(parsed.SeriesTitle, parsed.SeriesYear);
        if (context.Series.TryGetValue(key, out var existingId))
        {
            // Para refrescar sus imágenes locales al final del escaneo (una serie ya guardada no pasa por aquí al crearse).
            if (context.SeriesImages.ContainsKey(existingId))
            {
                context.SeriesFolders.TryAdd(existingId, SeriesFolder(file));
            }

            return existingId;
        }

        var series = new Series
        {
            Id = Guid.CreateVersion7(),
            LibraryId = context.Library.Id,
            Title = Truncate(parsed.SeriesTitle, MaxTitleLength),
            SortTitle = SortTitles.From(parsed.SeriesTitle),
            Year = parsed.SeriesYear,
            ImdbId = parsed.ImdbId,
            AniListId = parsed.AniListId,
            PosterPath = FindPoster(context, SeriesFolder(file)),
            BackdropPath = FindBackdrop(context, SeriesFolder(file)),
            ScanKey = Truncate(key, MaxScanKeyLength),
            DateAdded = DateTimeOffset.UtcNow,
            MetadataStatus = MetadataStatus.Pending,
        };
        db.MediaItems.Add(series);
        context.Series[key] = series.Id;
        return series.Id;
    }

    // Temporada o carpeta de una serie. Se busca por su título dentro de la serie: las de número se
    // llaman "Temporada N" o "Especiales", y las demás, como su carpeta ("Lat", "Sub").
    private Guid GetOrCreateSeason(ScanContext context, Guid seriesId, string title, int? seasonNumber, ScannedFile file)
    {
        var key = SeasonKey(seriesId, title);
        var posterPath = SeasonPoster(context, file);
        if (context.Seasons.TryGetValue(key, out var known))
        {
            if (known.PosterPath is null && posterPath is not null)
            {
                context.Seasons[key] = known with { PosterPath = posterPath };
                context.SeasonPosters[known.Id] = posterPath;
            }

            return known.Id;
        }

        var season = new Season
        {
            Id = Guid.CreateVersion7(),
            LibraryId = context.Library.Id,
            ParentId = seriesId,
            Title = Truncate(title, MaxTitleLength),
            SortTitle = SortTitles.From(title),
            SeasonNumber = seasonNumber,
            PosterPath = posterPath,
            DateAdded = DateTimeOffset.UtcNow,
            MetadataStatus = MetadataStatus.Pending,
        };
        db.MediaItems.Add(season);
        context.Seasons[key] = new KnownSeason(season.Id, posterPath);
        return season.Id;
    }

    private static string SeasonTitle(int seasonNumber) => seasonNumber == 0 ? "Especiales" : $"Temporada {seasonNumber}";

    private void AddTrack(ScanContext context, ScannedFile file)
    {
        var info = ReadTrackInfo(file);
        var albumId = GetOrCreateAlbum(context, file, info);

        var track = new Track
        {
            Id = Guid.CreateVersion7(),
            LibraryId = context.Library.Id,
            ParentId = albumId,
            Title = Truncate(info.Title, MaxTitleLength),
            SortTitle = SortTitles.From(info.Title),
            Year = info.Year,
            Genres = info.Genres,
            FilePath = file.FullPath,
            FileSize = file.Size,
            FileModifiedAt = file.ModifiedAt,
            DurationSeconds = info.DurationSeconds,
            TrackNumber = info.TrackNumber,
            DiscNumber = info.DiscNumber,
            Artist = TruncateOrNull(info.Artist, MaxArtistLength),
            DateAdded = DateTimeOffset.UtcNow,
            MetadataStatus = MetadataStatus.Pending,
        };
        db.MediaItems.Add(track);
    }

    private Guid GetOrCreateAlbum(ScanContext context, ScannedFile file, TrackInfo info)
    {
        var key = AlbumKey(info.AlbumArtist, info.Album);
        if (context.Albums.TryGetValue(key, out var existingId))
        {
            return existingId;
        }

        var albumId = Guid.CreateVersion7();
        var album = new MusicAlbum
        {
            Id = albumId,
            LibraryId = context.Library.Id,
            Title = Truncate(info.Album, MaxTitleLength),
            SortTitle = SortTitles.From(info.Album),
            Year = info.Year,
            Genres = info.Genres,
            AlbumArtist = TruncateOrNull(info.AlbumArtist, MaxArtistLength),
            PosterPath = FindPoster(context, AlbumFolder(file)) ?? ExtractCover(albumId, file.FullPath),
            DateAdded = DateTimeOffset.UtcNow,
            MetadataStatus = MetadataStatus.Pending,
        };
        db.MediaItems.Add(album);
        context.Albums[key] = albumId;
        return albumId;
    }

    // Etiquetas del archivo y, para lo que falte, lo que dice la ruta (Artista/Álbum (Año)/NN - Canción).
    private TrackInfo ReadTrackInfo(ScannedFile file)
    {
        var parsed = MusicNameParser.Parse(file.RelativePath);
        var tags = tagReader.ReadTags(file.FullPath);

        var title = tags?.Title ?? parsed.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(file.FullPath);
        }

        var artist = tags is { Artists.Count: > 0 } ? string.Join(", ", tags.Artists) : parsed.Artist;

        // Para agrupar en un álbum pesa más la carpeta del artista que el intérprete de cada pista
        // (en un disco de colaboraciones, cada pista tiene intérpretes distintos).
        var albumArtist = tags?.AlbumArtist ?? parsed.Artist ?? tags?.Artists.FirstOrDefault();

        return new TrackInfo(
            title,
            artist,
            albumArtist,
            tags?.Album ?? parsed.Album ?? "Álbum desconocido",
            tags?.Year ?? parsed.Year,
            tags?.TrackNumber ?? parsed.TrackNumber,
            tags?.DiscNumber ?? parsed.DiscNumber,
            tags?.Genres.ToList() ?? new List<string>(),
            tags?.DurationSeconds);
    }

    // ---------------------------------------------------------------- cambios

    private void UpdateItem(ScanContext context, PlayableItem item, ScannedFile file)
    {
        item.FileSize = file.Size;
        item.FileModifiedAt = file.ModifiedAt;

        if (item is Track track)
        {
            // Las etiquetas pudieron cambiar: la pista puede cambiar de título y hasta de álbum.
            var info = ReadTrackInfo(file);
            track.Title = Truncate(info.Title, MaxTitleLength);
            track.SortTitle = SortTitles.From(info.Title);
            track.Artist = TruncateOrNull(info.Artist, MaxArtistLength);
            track.TrackNumber = info.TrackNumber;
            track.DiscNumber = info.DiscNumber;
            track.Year = info.Year;
            track.Genres = info.Genres;
            track.DurationSeconds = info.DurationSeconds;
            track.ParentId = GetOrCreateAlbum(context, file, info);
        }
        else
        {
            item.DurationSeconds = tagReader.ReadDuration(file.FullPath) ?? item.DurationSeconds;
            if (item is Episode episode)
            {
                ApplyLayout(episode, DescribeEpisode(context, file));
            }
        }
    }

    // Episodios que ya estaban y no cambiaron: se calcula su lugar con las reglas de hoy y solo se
    // guardan los que quedaron distintos.
    private async Task RearrangeEpisodesAsync(
        ScanContext context,
        IEnumerable<ScannedFile> unchanged,
        Dictionary<string, ExistingFile> existing,
        CancellationToken cancellationToken)
    {
        var stored = await db.MediaItems.AsNoTracking().OfType<Episode>()
            .Where(item => item.LibraryId == context.Library.Id)
            .Select(item => new StoredEpisode(
                item.Id, item.ParentId, item.SeasonNumber, item.EpisodeNumber, item.AbsoluteNumber,
                item.Title, item.PosterPath, item.MetadataStatus))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var moves = new List<(Guid Id, EpisodeLayout Layout)>();
        foreach (var file in unchanged)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!stored.TryGetValue(existing[file.FullPath].Id, out var episode))
            {
                continue;
            }

            try
            {
                var layout = DescribeEpisode(context, file);
                if (!IsInPlace(episode, layout))
                {
                    moves.Add((episode.Id, layout));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordError(context, file, ex);
            }
        }

        foreach (var chunk in moves.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layouts = chunk.ToDictionary(move => move.Id, move => move.Layout);
            var ids = layouts.Keys.ToList();
            var episodes = await db.MediaItems.OfType<Episode>()
                .Where(item => ids.Contains(item.Id))
                .ToListAsync(cancellationToken);
            foreach (var episode in episodes)
            {
                ApplyLayout(episode, layouts[episode.Id]);
                context.Job.Updated++;
            }

            await FlushAsync(context, cancellationToken);
        }

        // Los padres nuevos (por ejemplo, las carpetas Lat y Sub) se guardan aunque no hubiera cambios.
        await FlushAsync(context, cancellationToken);
    }

    // Con metadatos del proveedor (fase 4), el título ya no sale del nombre del archivo y la imagen
    // del proveedor se conserva si junto al video no hay miniatura.
    private static void ApplyLayout(Episode episode, EpisodeLayout layout)
    {
        episode.ParentId = layout.ParentId;
        episode.SeasonNumber = layout.SeasonNumber;
        episode.EpisodeNumber = layout.EpisodeNumber;
        episode.AbsoluteNumber = layout.AbsoluteNumber;

        var matched = episode.MetadataStatus == MetadataStatus.Matched;
        if (!matched)
        {
            episode.Title = layout.Title;
            episode.SortTitle = SortTitles.From(layout.Title);
        }

        if (layout.PosterPath is not null || !matched)
        {
            episode.PosterPath = layout.PosterPath;
        }
    }

    // Lo mismo que ApplyLayout, como comparación: true si aplicarlo no cambiaría nada.
    private static bool IsInPlace(StoredEpisode episode, EpisodeLayout layout)
    {
        var matched = episode.MetadataStatus == MetadataStatus.Matched;
        return episode.ParentId == layout.ParentId
            && episode.SeasonNumber == layout.SeasonNumber
            && episode.EpisodeNumber == layout.EpisodeNumber
            && episode.AbsoluteNumber == layout.AbsoluteNumber
            && (matched || episode.Title == layout.Title)
            && ((layout.PosterPath is null && matched) || episode.PosterPath == layout.PosterPath);
    }

    // ---------------------------------------------------------------- guardado

    // Guarda el lote y limpia el rastreador de cambios para que no crezca con la biblioteca.
    private async Task FlushAsync(ScanContext context, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        context.Pending = 0;
        await SaveJobAsync(context.Job, cancellationToken);
    }

    // El ScanJob se lee sin rastreo y se actualiza con ExecuteUpdate: sobrevive a ChangeTracker.Clear().
    private Task<int> SaveJobAsync(ScanJob job, CancellationToken cancellationToken) =>
        db.ScanJobs
            .Where(j => j.Id == job.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, job.Status)
                    .SetProperty(j => j.StartedAt, job.StartedAt)
                    .SetProperty(j => j.FinishedAt, job.FinishedAt)
                    .SetProperty(j => j.Added, job.Added)
                    .SetProperty(j => j.Updated, job.Updated)
                    .SetProperty(j => j.Removed, job.Removed)
                    .SetProperty(j => j.Errors, job.Errors)
                    .SetProperty(j => j.ErrorMessage, job.ErrorMessage),
                cancellationToken);

    private async Task RefreshLocalImagesAsync(
        ScanContext context,
        List<ScannedFile> files,
        Dictionary<string, ExistingFile> existing,
        CancellationToken cancellationToken)
    {
        foreach (var (seriesId, folder) in context.SeriesFolders)
        {
            if (folder is null || !context.SeriesImages.TryGetValue(seriesId, out var known))
            {
                continue;
            }

            await UpdateImagesAsync(seriesId, known, FindPoster(context, folder), FindBackdrop(context, folder), cancellationToken);
        }

        if (context.Library.Type != LibraryType.Movies)
        {
            return;
        }

        var stored = await db.MediaItems.AsNoTracking().OfType<Movie>()
            .Where(item => item.LibraryId == context.Library.Id)
            .Select(item => new { item.Id, item.PosterPath, item.BackdropPath })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var file in files)
        {
            if (!IsInSubfolder(file) ||
                !existing.TryGetValue(file.FullPath, out var movie) ||
                !stored.TryGetValue(movie.Id, out var known))
            {
                continue;
            }

            var folder = Path.GetDirectoryName(file.FullPath);
            await UpdateImagesAsync(
                movie.Id,
                new KnownImages(known.PosterPath, known.BackdropPath),
                FindPoster(context, folder),
                FindBackdrop(context, folder),
                cancellationToken);
        }
    }

    private async Task UpdateImagesAsync(
        Guid id, KnownImages known, string? poster, string? backdrop, CancellationToken cancellationToken)
    {
        if (poster is not null && poster != known.PosterPath)
        {
            await db.MediaItems
                .Where(item => item.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PosterPath, poster), cancellationToken);
        }

        if (backdrop is not null && backdrop != known.BackdropPath)
        {
            await db.MediaItems
                .Where(item => item.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.BackdropPath, backdrop), cancellationToken);
        }
    }

    private async Task DeleteEmptyParentsAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        await db.MediaItems.OfType<Season>()
            .Where(season => season.LibraryId == libraryId && !db.MediaItems.Any(child => child.ParentId == season.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await db.MediaItems.OfType<Series>()
            .Where(series => series.LibraryId == libraryId && !db.MediaItems.Any(child => child.ParentId == series.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await db.MediaItems.OfType<MusicAlbum>()
            .Where(album => album.LibraryId == libraryId && !db.MediaItems.Any(child => child.ParentId == album.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private void RecordError(ScanContext context, ScannedFile file, Exception exception)
    {
        context.Job.Errors++;
        logger.LogWarning(exception, "No se pudo procesar {Path}.", file.FullPath);
    }

    // ---------------------------------------------------------------- imágenes

    private static bool IsInSubfolder(ScannedFile file) =>
        file.RelativePath.Contains(Path.DirectorySeparatorChar) || file.RelativePath.Contains(Path.AltDirectorySeparatorChar);

    // Carpeta de la serie: la primera de la ruta relativa, salvo que sea una carpeta de temporada.
    private static string? SeriesFolder(ScannedFile file)
    {
        var separator = file.RelativePath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (separator <= 0)
        {
            return null;
        }

        var first = file.RelativePath[..separator];
        return EpisodeNameParser.ParseSeasonFolder(first) is null
            ? Path.Combine(file.FullPath[..^file.RelativePath.Length], first)
            : null;
    }

    // Carpeta del álbum: la del archivo, o la de arriba si es una carpeta de disco (CD1, Disc 2…).
    private static string? AlbumFolder(ScannedFile file)
    {
        if (!IsInSubfolder(file))
        {
            return null;
        }

        var folder = Path.GetDirectoryName(file.FullPath);
        if (folder is not null && MusicNameParser.IsDiscFolder(Path.GetFileName(folder), out _))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder;
    }

    private string? FindPoster(ScanContext context, string? folder)
    {
        if (folder is null)
        {
            return null;
        }

        if (context.Posters.TryGetValue(folder, out var cached))
        {
            return cached;
        }

        var names = FolderFileNames(context, folder);
        string? found = null;
        foreach (var candidate in PosterCandidates)
        {
            if (names.TryGetValue(candidate, out var actual))
            {
                found = Path.Combine(folder, actual);
                break;
            }
        }

        context.Posters[folder] = found;
        return found;
    }

    private string? FindBackdrop(ScanContext context, string? folder)
    {
        if (folder is null)
        {
            return null;
        }

        var names = FolderFileNames(context, folder);
        foreach (var candidate in BackdropCandidates)
        {
            if (names.TryGetValue(candidate, out var actual))
            {
                return Path.Combine(folder, actual);
            }
        }

        return null;
    }

    // Imagen de una temporada o carpeta: la suya ("Lat/folder.jpg") o, si no tiene, la de la serie.
    private string? SeasonPoster(ScanContext context, ScannedFile file) =>
        IsInSubfolder(file)
            ? FindPoster(context, Path.GetDirectoryName(file.FullPath)) ?? FindPoster(context, SeriesFolder(file))
            : null;

    private string? FindThumbnail(ScanContext context, ScannedFile file)
    {
        var folder = Path.GetDirectoryName(file.FullPath);
        if (folder is null)
        {
            return null;
        }

        var names = FolderFileNames(context, folder);
        var stem = Path.GetFileNameWithoutExtension(file.FullPath);
        foreach (var suffix in ThumbnailSuffixes)
        {
            if (names.TryGetValue(stem + suffix, out var actual))
            {
                return Path.Combine(folder, actual);
            }
        }

        return null;
    }

    // Nombres de los archivos de una carpeta, sin distinguir mayúsculas, con el nombre tal como está en
    // el disco. Se lee una vez por carpeta en cada escaneo.
    private Dictionary<string, string> FolderFileNames(ScanContext context, string folder)
    {
        if (context.FolderFiles.TryGetValue(folder, out var cached))
        {
            return cached;
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(path);
                names.TryAdd(name, name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "No se pudieron leer los archivos de {Path}.", folder);
        }

        context.FolderFiles[folder] = names;
        return names;
    }

    // Guarda la carátula incrustada del archivo en la carpeta de datos: /media es de solo lectura.
    private string? ExtractCover(Guid albumId, string audioPath)
    {
        var cover = tagReader.ReadCover(audioPath);
        if (cover is null)
        {
            return null;
        }

        try
        {
            var extension = cover.MimeType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            Directory.CreateDirectory(storage.Value.ImagesPath);
            var target = Path.Combine(storage.Value.ImagesPath, $"{albumId}{extension}");
            File.WriteAllBytes(target, cover.Data);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "No se pudo guardar la carátula de {Path}.", audioPath);
            return null;
        }
    }

    // ---------------------------------------------------------------- claves y utilidades

    private static string SeasonKey(Guid seriesId, string title) => $"{seriesId:N}|{title.ToLowerInvariant()}";

    private static string AlbumKey(string? albumArtist, string album) =>
        $"{(albumArtist ?? "").ToLowerInvariant()}|{album.ToLowerInvariant()}";

    private static string Truncate(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength] : text;

    private static string? TruncateOrNull(string? text, int maxLength) =>
        text is null ? null : Truncate(text, maxLength);

    private sealed record ScannedFile(string FullPath, string RelativePath, long Size, DateTimeOffset ModifiedAt);

    private sealed record ExistingFile(Guid Id, string FilePath, long FileSize, DateTimeOffset FileModifiedAt);

    /// <summary>Lugar y presentación de un episodio que salen de su ruta.</summary>
    private sealed record EpisodeLayout(
        Guid ParentId, int? SeasonNumber, int? EpisodeNumber, int? AbsoluteNumber, string Title, string? PosterPath);

    /// <summary>Lo que hay guardado de un episodio, para compararlo con su <see cref="EpisodeLayout"/>.</summary>
    private sealed record StoredEpisode(
        Guid Id,
        Guid? ParentId,
        int? SeasonNumber,
        int? EpisodeNumber,
        int? AbsoluteNumber,
        string Title,
        string? PosterPath,
        MetadataStatus MetadataStatus);

    /// <summary>Temporada conocida en este escaneo: su id y su imagen.</summary>
    private sealed record KnownSeason(Guid Id, string? PosterPath);

    private sealed record KnownImages(string? PosterPath, string? BackdropPath);

    private sealed record TrackInfo(
        string Title,
        string? Artist,
        string? AlbumArtist,
        string Album,
        int? Year,
        int? TrackNumber,
        int? DiscNumber,
        List<string> Genres,
        double? DurationSeconds);

    /// <summary>Estado de un escaneo: la biblioteca, el avance y los padres ya conocidos (solo ids, para no depender del rastreador).</summary>
    private sealed class ScanContext(Library library, ScanJob job)
    {
        public Library Library { get; } = library;

        public ScanJob Job { get; } = job;

        public int Pending { get; set; }

        public List<string> UnreadableRoots { get; } = [];

        public Dictionary<string, Guid> Series { get; } = new();

        /// <summary>Imágenes que tienen guardadas las series que ya existían, para actualizarlas solo si cambiaron.</summary>
        public Dictionary<Guid, KnownImages> SeriesImages { get; } = new();

        /// <summary>Carpeta de las series que ya existían, por si ahora tienen un póster o un fondo local.</summary>
        public Dictionary<Guid, string?> SeriesFolders { get; } = new();

        public Dictionary<string, KnownSeason> Seasons { get; } = new();

        /// <summary>Temporadas que ya existían y ahora tienen imagen: se guardan al final.</summary>
        public Dictionary<Guid, string> SeasonPosters { get; } = new();

        public Dictionary<string, Dictionary<string, string>> FolderFiles { get; } = new(MediaPaths.Comparer);

        public Dictionary<string, Guid> Albums { get; } = new();

        public Dictionary<string, string?> Posters { get; } = new(MediaPaths.Comparer);
    }
}
