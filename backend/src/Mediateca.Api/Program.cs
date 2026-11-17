using System.Text.Json.Serialization;
using Mediateca.Api.Auth;
using Mediateca.Api.Items;
using Mediateca.Api.Libraries;
using Mediateca.Api.Setup;
using Mediateca.Infrastructure;
using Mediateca.Infrastructure.Persistence;
using Mediateca.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
// Los enums viajan como texto ("Movies", "Completed"…) en las respuestas y en los cuerpos de las solicitudes.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMediatecaAuth();

var app = builder.Build();

// Primer arranque: crea la carpeta de datos y la base de datos (o aplica las migraciones
// pendientes) y siembra los roles. Las herramientas de EF construyen la app sin correrla.
if (!EF.IsDesignTime)
{
    app.Services.EnsureDataFolder();
    await app.Services.MigrateDatabaseAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseXsrfValidation();

if (app.Environment.IsDevelopment())
{
    // Documento OpenAPI en /openapi/v1.json y explorador en /scalar.
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
    .WithName("Health")
    .WithSummary("Indica si la API está respondiendo.")
    .AllowAnonymous();

app.MapSetupEndpoints();
app.MapAuthEndpoints();
app.MapAdminLibraryEndpoints();
app.MapAdminScanScheduleEndpoints();
app.MapLibraryEndpoints();
app.MapItemEndpoints();

await app.RunAsync();

/// <summary>Respuesta de <c>GET /api/health</c>.</summary>
public sealed record HealthResponse(string Status, DateTimeOffset Time);

/// <summary>Visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
