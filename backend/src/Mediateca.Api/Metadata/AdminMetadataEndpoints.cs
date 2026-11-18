using Mediateca.Api.Auth;
using Mediateca.Core.Media;
using Mediateca.Core.Metadata;
using Mediateca.Infrastructure.Metadata;
using Mediateca.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Mediateca.Api.Metadata;

/// <summary>
/// Metadatos desde la administración: poner en la cola lo pendiente de una biblioteca, y buscar, identificar y
/// actualizar un elemento. Solo para administradores.
/// <list type="bullet">
/// <item>La cola de una biblioteca solo se marca aquí; la procesa el worker.</item>
/// <item>Identificar y Actualizar piden los datos <b>ahora</b>, durante la solicitud: el administrador está mirando
/// el diálogo y espera ver el resultado. Es una solicitud (más la descarga de imágenes), no la cola.</item>
/// </list>
/// </summary>
public static class AdminMetadataEndpoints
{
    private const int MaxQueryLength = 200;

    public static IEndpointRouteBuilder MapAdminMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Administración de metadatos")
            .RequireAuthorization(AuthSetup.AdminPolicy);

        group.MapPost("/libraries/{id:guid}/metadata/fetch", FetchAsync)
            .WithSummary("Pone en la cola de metadatos lo pendiente y lo que dio error de una biblioteca (202); lo procesa el worker.");

        group.MapGet("/items/{id:guid}/metadata/search", SearchAsync)
            .WithSummary("Busca en el proveedor de la biblioteca (AniList u OMDb) para el diálogo Identificar.");

        group.MapPost("/items/{id:guid}/metadata/identify", IdentifyAsync)
            .WithSummary("Aplica el resultado elegido: un id o una dirección de AniList (anilist.co/anime/21) o de IMDb (tt0118799). Pide los datos en el momento.");

        group.MapPost("/items/{id:guid}/metadata/refresh", RefreshAsync)
            .WithSummary("Vuelve a pedir los metadatos de la serie o película ignorando la caché. Pide los datos en el momento.");

        return app;
    }

    private static async Task<Results<Accepted<MetadataQueueResponse>, NotFound, ProblemHttpResult>> FetchAsync(
        Guid id, MediatecaDbContext db, ProviderStatusService providers, CancellationToken cancellationToken)
    {
        var library = await db.Libraries.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (library is null)
        {
            return TypedResults.NotFound();
        }

        var providerName = MetadataProviders.For(library.Type);
        if (providerName is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Las bibliotecas de música no tienen metadatos.",
                detail: "Los metadatos de AniList y OMDb son para anime, series y películas.");
        }

        // Los errores vuelven a la cola; lo que no se encontró no (se arregla con Identificar o cambiando el nombre de la carpeta).
        await db.MediaItems
            .Where(item => item.LibraryId == id && item.ParentId == null && item.MetadataStatus == MetadataStatus.Error)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.MetadataStatus, MetadataStatus.Pending)
                    .SetProperty(item => item.MetadataMessage, (string?)null),
                cancellationToken);

        var queued = await db.MediaItems.CountAsync(
            item => item.LibraryId == id &&
                    item.ParentId == null &&
                    item.MetadataStatus == MetadataStatus.Pending &&
                    (item is Series || item is Movie),
            cancellationToken);

        var status = (await providers.GetAsync(cancellationToken)).FirstOrDefault(p => p.Provider == providerName);
        return TypedResults.Accepted(
            $"/api/admin/libraries/{id}",
            new MetadataQueueResponse(queued, status?.Configured ?? false, status?.Message));
    }

    private static async Task<Results<Ok<MetadataSearchResponse>, NotFound, ProblemHttpResult>> SearchAsync(
        Guid id, string? query, int? year, MetadataService service, MediatecaDbContext db, CancellationToken cancellationToken)
    {
        if (query is { Length: > MaxQueryLength })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "La búsqueda es demasiado larga.");
        }

        try
        {
            var found = await service.SearchAsync(id, query, year is > 1800 and < 2200 ? year : null, cancellationToken);
            if (found is null)
            {
                return TypedResults.NotFound();
            }

            var type = await db.MediaItems.AsNoTracking()
                .Where(item => item.Id == id)
                .Select(item => item.Library!.Type)
                .FirstAsync(cancellationToken);
            var provider = MetadataProviders.For(type)!;
            return TypedResults.Ok(new MetadataSearchResponse(
                provider,
                MetadataProviders.DisplayName(provider),
                found.Select(c => new MetadataCandidate(
                    c.ExternalId, c.Title, c.OriginalTitle, c.Year, c.EndYear, c.Format, c.Status?.ToString(), c.PosterUrl, c.Url))
                    .ToList()));
        }
        catch (Exception ex) when (ex is MetadataInputException or MetadataProviderException)
        {
            return Failure(ex);
        }
    }

    private static async Task<Results<Ok<MetadataResultResponse>, NotFound, ProblemHttpResult>> IdentifyAsync(
        Guid id, IdentifyRequest request, MetadataService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Identifier))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Escribe un id o una dirección de AniList o de IMDb.");
        }

        try
        {
            var outcome = await service.IdentifyAsync(id, request.Identifier, cancellationToken);
            return outcome is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(outcome));
        }
        catch (Exception ex) when (ex is MetadataInputException or MetadataProviderException)
        {
            return Failure(ex);
        }
    }

    private static async Task<Results<Ok<MetadataResultResponse>, NotFound, ProblemHttpResult>> RefreshAsync(
        Guid id, MetadataService service, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await service.MatchAsync(id, refresh: true, cancellationToken);
            return outcome is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(outcome));
        }
        catch (Exception ex) when (ex is MetadataInputException or MetadataProviderException)
        {
            return Failure(ex);
        }
    }

    private static MetadataResultResponse ToResponse(MetadataOutcome outcome) =>
        new(outcome.Status.ToString(), outcome.Message, outcome.Title);

    // 400: lo pedido no sirve; 409: falta la clave; 429: el proveedor pidió esperar; 502: el proveedor falló.
    private static ProblemHttpResult Failure(Exception exception) => exception switch
    {
        MetadataInputException => TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest, title: exception.Message),
        ProviderNotConfiguredException => TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict, title: exception.Message),
        ProviderRateLimitedException or ProviderQuotaExceededException => TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests, title: exception.Message),
        _ => TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: exception.Message),
    };
}
