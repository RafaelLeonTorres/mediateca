using Mediateca.Core.Libraries;
using Mediateca.Core.Scanning;

namespace Mediateca.Api.Libraries;

/// <summary>Cuerpo de POST y PUT /api/admin/libraries. En PUT, el tipo es opcional y no puede cambiar.</summary>
public sealed record LibraryRequest(string? Name, LibraryType? Type, List<string>? Paths);

/// <summary>Avance de un escaneo.</summary>
public sealed record ScanJobResponse(
    Guid Id,
    Guid LibraryId,
    ScanStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Added,
    int Updated,
    int Removed,
    int Errors,
    string? ErrorMessage)
{
    public static ScanJobResponse From(ScanJob job) => new(
        job.Id, job.LibraryId, job.Status, job.CreatedAt, job.StartedAt, job.FinishedAt,
        job.Added, job.Updated, job.Removed, job.Errors, job.ErrorMessage);
}

/// <summary>Respuesta de POST /api/admin/libraries/scan-all: cuántos escaneos se pusieron en la cola.</summary>
public sealed record ScanAllResponse(int Queued);

/// <summary>Cuerpo de PUT /api/admin/scan-schedule: si el escaneo automático está activo y su cron de Quartz.</summary>
public sealed record ScanScheduleRequest(bool? Enabled, string? CronExpression);

/// <summary>
/// Biblioteca para el administrador: con sus carpetas, la cantidad de elementos y el último escaneo.
/// <c>Paths</c> son las rutas del contenedor; <c>DisplayPaths</c> son las mismas, en el mismo orden, como
/// se muestran al usuario (F:\Peliculas).
/// </summary>
public sealed record AdminLibraryResponse(
    Guid Id,
    string Name,
    LibraryType Type,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> DisplayPaths,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastScannedAt,
    int ItemCount,
    ScanJobResponse? LastScan);

/// <summary>Biblioteca para cualquier usuario. No incluye las carpetas del servidor.</summary>
public sealed record LibraryResponse(Guid Id, string Name, LibraryType Type, int ItemCount);

/// <summary>Géneros y años que existen en una biblioteca, para los filtros de la cuadrícula.</summary>
public sealed record LibraryFiltersResponse(IReadOnlyList<string> Genres, IReadOnlyList<int> Years);
