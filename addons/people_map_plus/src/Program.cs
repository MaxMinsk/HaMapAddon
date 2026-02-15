using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        service = "people-map-plus-addon",
        utc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/people_map_plus/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        backend = "csharp",
        version = "0.1.2"
    });
});

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        name = "People Map Plus Backend",
        message = "Add-on is running. API scaffold only."
    });
});

app.Run();
