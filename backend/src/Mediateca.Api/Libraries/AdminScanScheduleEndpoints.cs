using Mediateca.Api.Auth;
using Mediateca.Infrastructure.Scanning;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mediateca.Api.Libraries;

/// <summary>Horario del escaneo automático de las bibliotecas. Solo para administradores.</summary>
public static class AdminScanScheduleEndpoints
{
    public static IEndpointRouteBuilder MapAdminScanScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Administración de bibliotecas")
            .RequireAuthorization(AuthSetup.AdminPolicy);

        group.MapGet("/scan-schedule", GetAsync)
            .WithSummary("Horario del escaneo automático: si está activo, su cron, la zona horaria y las próximas ejecuciones.");

        group.MapPut("/scan-schedule", UpdateAsync)
            .WithSummary("Cambia el horario. El worker lo aplica en menos de un minuto, sin reiniciarse.");

        return app;
    }

    private static async Task<Ok<ScanScheduleDescription>> GetAsync(
        ScanScheduleService schedules, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetAsync(cancellationToken);
        return TypedResults.Ok(schedules.Describe(schedule));
    }

    private static async Task<Results<Ok<ScanScheduleDescription>, ValidationProblem>> UpdateAsync(
        ScanScheduleRequest request, ScanScheduleService schedules, CancellationToken cancellationToken)
    {
        if (request.Enabled is not { } enabled)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["enabled"] = ["Indica si el escaneo automático está activado."],
            });
        }

        var result = await schedules.UpdateAsync(enabled, request.CronExpression, cancellationToken);
        if (result.Schedule is not { } schedule)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["cronExpression"] = [result.Error ?? "El horario no es válido."],
            });
        }

        return TypedResults.Ok(schedules.Describe(schedule));
    }
}
