using Mediateca.Api.Auth;
using Mediateca.Core.Libraries;
using Mediateca.Core.Scanning;
using Mediateca.Api.Metadata;
using Mediateca.Infrastructure.Libraries;
using Mediateca.Infrastructure.Metadata;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Scanning;
using Mediateca.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mediateca.Api.Libraries;

/// <summary>Administración de bibliotecas y escaneos. Solo para administradores.</summary>
public static class AdminLibraryEndpoints
{
    private const int MaxNameLength = 100;

    public static IEndpointRouteBuilder MapAdminLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Administración de bibliotecas")
            .RequireAuthorization(AuthSetup.AdminPolicy);

        group.MapGet("/libraries", ListAsync)
            .WithSummary("Lista las bibliotecas con sus carpetas, su cantidad de elementos y su último escaneo.");

        group.MapPost("/libraries", CreateAsync)
            .WithSummary("Crea una biblioteca. Las carpetas tienen que existir y estar dentro de las carpetas de medios.");

        group.MapPut("/libraries/{id:guid}", UpdateAsync)
            .WithSummary("Cambia el nombre y las carpetas de una biblioteca. El tipo no se puede cambiar.");

        group.MapDelete("/libraries/{id:guid}", DeleteAsync)
            .WithSummary("Elimina una biblioteca y todo su contenido. Responde 409 mientras se escanea.");

        group.MapPost("/libraries/{id:guid}/scan", ScanAsync)
            .WithSummary("Pone un escaneo en la cola (202); lo corre el worker. Si ya hay uno en la cola o corriendo, devuelve ese.");

        group.MapPost("/libraries/scan-all", ScanAllAsync)
            .WithSummary("Pide un escaneo de cada biblioteca (202). Las que ya tienen uno en la cola o corriendo no cuentan.");

        group.MapGet("/scans/{id:guid}", GetScanAsync)
            .WithSummary("Avance de un escaneo.");

        group.MapGet("/folders", BrowseFolders)
            .WithSummary("Selector de carpetas. Sin path lista las carpetas de medios y los discos; con path, sus subcarpetas.");

        return app;
    }

    private static async Task<Ok<List<AdminLibraryResponse>>> ListAsync(
        MediatecaDbContext db, IOptions<StorageOptions> storage, ProviderStatusService providers, CancellationToken cancellationToken)
    {
        var libraries = await db.Libraries.AsNoTracking().OrderBy(l => l.Name).ToListAsync(cancellationToken);
        var counts = await db.CountTopLevelAsync(cancellationToken);
        var metadata = await db.MetadataSummariesAsync(providers, libraries, cancellationToken);

        var responses = new List<AdminLibraryResponse>();
        foreach (var library in libraries)
        {
            var lastScan = await LastScanAsync(db, library.Id, cancellationToken);
            responses.Add(ToResponse(
                library, counts.GetValueOrDefault(library.Id), lastScan, storage.Value.DrivesPath, metadata.GetValueOrDefault(library.Id)));
        }

        return TypedResults.Ok(responses);
    }

    private static async Task<Results<Created<AdminLibraryResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        LibraryRequest request,
        MediatecaDbContext db,
        LibraryPathValidator pathValidator,
        IOptions<StorageOptions> storage,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var name = ValidateName(request.Name, errors);

        var type = request.Type;
        if (type is null || !Enum.IsDefined(type.Value))
        {
            errors["type"] = ["Elige el tipo de biblioteca: películas, series, anime o música."];
        }

        var paths = pathValidator.Validate(request.Paths);
        if (!paths.IsValid)
        {
            errors["paths"] = paths.Errors.ToArray();
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await NameExistsAsync(db, name, Guid.Empty, cancellationToken))
        {
            return DuplicateName(name);
        }

        var library = new Library
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Type = type.GetValueOrDefault(),
            Paths = paths.Paths.ToList(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Libraries.Add(library);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Si otra solicitud creó una biblioteca con ese nombre justo antes, el índice único lo rechaza.
            if (await NameExistsAsync(db, name, library.Id, cancellationToken))
            {
                return DuplicateName(name);
            }

            throw;
        }

        return TypedResults.Created($"/api/admin/libraries/{library.Id}", ToResponse(library, 0, null, storage.Value.DrivesPath));
    }

    private static async Task<Results<Ok<AdminLibraryResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        LibraryRequest request,
        MediatecaDbContext db,
        LibraryPathValidator pathValidator,
        IOptions<StorageOptions> storage,
        ProviderStatusService providers,
        CancellationToken cancellationToken)
    {
        var library = await db.Libraries.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (library is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        var name = ValidateName(request.Name, errors);

        if (request.Type is { } type && type != library.Type)
        {
            errors["type"] = ["El tipo de una biblioteca no se puede cambiar. Crea otra del tipo que necesitas."];
        }

        var paths = pathValidator.Validate(request.Paths);
        if (!paths.IsValid)
        {
            errors["paths"] = paths.Errors.ToArray();
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await NameExistsAsync(db, name, library.Id, cancellationToken))
        {
            return DuplicateName(name);
        }

        library.Name = name;
        library.Paths = paths.Paths.ToList();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await NameExistsAsync(db, name, library.Id, cancellationToken))
            {
                return DuplicateName(name);
            }

            throw;
        }

        var counts = await db.CountTopLevelAsync(cancellationToken);
        var lastScan = await LastScanAsync(db, library.Id, cancellationToken);
        var metadata = await db.MetadataSummariesAsync(providers, [library], cancellationToken);
        return TypedResults.Ok(ToResponse(
            library, counts.GetValueOrDefault(library.Id), lastScan, storage.Value.DrivesPath, metadata.GetValueOrDefault(library.Id)));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id, MediatecaDbContext db, ScanRequestService scans, CancellationToken cancellationToken)
    {
        if (!await db.Libraries.AnyAsync(l => l.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (await scans.HasActiveScanAsync(id, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "No se puede eliminar la biblioteca mientras se escanea.",
                detail: "Espera a que termine el escaneo e inténtalo de nuevo.");
        }

        // Los elementos y los escaneos de la biblioteca se borran en cascada en la base de datos.
        await db.Libraries.Where(l => l.Id == id).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Accepted<ScanJobResponse>, NotFound>> ScanAsync(
        Guid id, ScanRequestService scans, CancellationToken cancellationToken)
    {
        var result = await scans.RequestAsync(id, cancellationToken);
        if (result is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Accepted($"/api/admin/scans/{result.Job.Id}", ScanJobResponse.From(result.Job));
    }

    private static async Task<Accepted<ScanAllResponse>> ScanAllAsync(
        ScanRequestService scans, CancellationToken cancellationToken)
    {
        var queued = await scans.RequestAllAsync(cancellationToken);
        return TypedResults.Accepted("/api/admin/libraries", new ScanAllResponse(queued));
    }

    private static async Task<Results<Ok<ScanJobResponse>, NotFound>> GetScanAsync(
        Guid id, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        var job = await db.ScanJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        return job is null ? TypedResults.NotFound() : TypedResults.Ok(ScanJobResponse.From(job));
    }

    private static Results<Ok<FolderListing>, ProblemHttpResult> BrowseFolders(string? path, FolderBrowser browser)
    {
        var result = browser.Browse(path);
        return result.Listing is { } listing
            ? TypedResults.Ok(listing)
            : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Error);
    }

    private static string ValidateName(string? requested, Dictionary<string, string[]> errors)
    {
        var name = requested?.Trim() ?? "";
        if (name.Length == 0)
        {
            errors["name"] = ["Escribe un nombre para la biblioteca."];
        }
        else if (name.Length > MaxNameLength)
        {
            errors["name"] = [$"El nombre puede tener hasta {MaxNameLength} caracteres."];
        }

        return name;
    }

    // Sin distinguir mayúsculas: "Películas" y "películas" serían confusas en el selector.
    private static Task<bool> NameExistsAsync(MediatecaDbContext db, string name, Guid excludeId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLowerInvariant();
        return db.Libraries.AnyAsync(l => l.Name.ToLower() == lowered && l.Id != excludeId, cancellationToken);
    }

    private static ProblemHttpResult DuplicateName(string name) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: $"Ya existe una biblioteca llamada '{name}'.",
            detail: "Elige otro nombre.");

    private static Task<ScanJob?> LastScanAsync(MediatecaDbContext db, Guid libraryId, CancellationToken cancellationToken) =>
        db.ScanJobs.AsNoTracking()
            .Where(j => j.LibraryId == libraryId)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static AdminLibraryResponse ToResponse(
        Library library, int itemCount, ScanJob? lastScan, string drivesPath, MetadataSummary? metadata = null) =>
        new(
            library.Id,
            library.Name,
            library.Type,
            library.Paths,
            library.Paths.Select(path => MediaPaths.ToDisplayPath(path, drivesPath)).ToList(),
            library.CreatedAt,
            library.LastScannedAt,
            itemCount,
            lastScan is null ? null : ScanJobResponse.From(lastScan),
            metadata);
}
