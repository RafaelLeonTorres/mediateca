using Mediateca.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Documento OpenAPI en /openapi/v1.json y explorador en /scalar.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
    .WithName("Health")
    .WithSummary("Indica si la API está respondiendo.");

app.Run();

/// <summary>Respuesta de <c>GET /api/health</c>.</summary>
public sealed record HealthResponse(string Status, DateTimeOffset Time);

/// <summary>Visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
