using System.Text.Json;
using PeopleMapPlus.Addon;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddHttpClient(nameof(OneDriveSyncOrchestrator), client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddSingleton<AddonOptionsProvider>();
builder.Services.AddSingleton<SyncRepository>();
builder.Services.AddSingleton<OneDriveSyncOrchestrator>();
builder.Services.AddHostedService<OneDriveSyncWorker>();

var app = builder.Build();

app.MapGet("/health", (OneDriveSyncOrchestrator orchestrator) =>
{
    return Results.Ok(new
    {
        status = "ok",
        service = "people-map-plus-addon",
        utc = DateTimeOffset.UtcNow,
        lastSync = orchestrator.GetLastResult()
    });
});

app.MapGet("/api/people_map_plus/health", (OneDriveSyncOrchestrator orchestrator) =>
{
    return Results.Ok(new
    {
        status = "ok",
        backend = "csharp",
        version = "0.1.3",
        lastSync = orchestrator.GetLastResult()
    });
});

app.MapGet("/api/people_map_plus/sync/status", (
    AddonOptionsProvider optionsProvider,
    OneDriveSyncOrchestrator orchestrator) =>
{
    var options = optionsProvider.Load();
    return Results.Ok(new
    {
        oneDriveEnabled = options.OneDriveEnabled,
        lookbackDays = options.LookbackDays,
        syncIntervalHours = options.SyncIntervalHours,
        destinationSubdir = options.DestinationSubdir,
        runSyncOnStartup = options.RunSyncOnStartup,
        lastSync = orchestrator.GetLastResult()
    });
});

app.MapPost("/api/people_map_plus/sync/run", async (OneDriveSyncOrchestrator orchestrator, CancellationToken cancellationToken) =>
{
    var result = await orchestrator.RunOnceAsync("manual", cancellationToken);
    var statusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;
    return Results.Json(result, statusCode: statusCode);
});

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        name = "People Map Plus Backend",
        message = "Add-on is running.",
        endpoints = new[]
        {
            "/health",
            "/api/people_map_plus/health",
            "/api/people_map_plus/sync/status",
            "/api/people_map_plus/sync/run"
        }
    });
});

app.Run();
