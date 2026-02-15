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
builder.Services.AddHttpClient(nameof(OneDriveDeviceAuthService), client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddSingleton<AddonOptionsProvider>();
builder.Services.AddSingleton<OneDriveTokenStore>();
builder.Services.AddSingleton<SyncRepository>();
builder.Services.AddSingleton<OneDriveSyncOrchestrator>();
builder.Services.AddSingleton<OneDriveDeviceAuthService>();
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
        version = "0.1.16",
        lastSync = orchestrator.GetLastResult()
    });
});

app.MapGet("/api/people_map_plus/sync/status", async (
    AddonOptionsProvider optionsProvider,
    OneDriveSyncOrchestrator orchestrator,
    OneDriveTokenStore tokenStore,
    CancellationToken cancellationToken) =>
{
    var options = optionsProvider.Load();
    var hasStoredRefreshToken = await tokenStore.HasRefreshTokenAsync(cancellationToken);
    return Results.Ok(new
    {
        oneDriveEnabled = options.OneDriveEnabled,
        lookbackDays = options.LookbackDays,
        syncIntervalHours = options.SyncIntervalHours,
        maxSize = options.MaxSize,
        destinationSubdir = options.DestinationSubdir,
        runSyncOnStartup = options.RunSyncOnStartup,
        hasStoredRefreshToken,
        lastSync = orchestrator.GetLastResult()
    });
});

app.MapPost("/api/people_map_plus/sync/run", async (OneDriveSyncOrchestrator orchestrator, CancellationToken cancellationToken) =>
{
    var result = await orchestrator.RunOnceAsync("manual", cancellationToken);
    var statusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;
    return Results.Json(result, statusCode: statusCode);
});

app.MapGet("/api/people_map_plus/onedrive/folders", async (
    string? path,
    OneDriveSyncOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    var result = await orchestrator.ListFoldersAsync(path, cancellationToken);
    var statusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
    return Results.Json(result, statusCode: statusCode);
});

app.MapGet("/api/people_map_plus/onedrive/device/status", async (
    OneDriveDeviceAuthService deviceAuthService,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await deviceAuthService.GetStatusAsync(cancellationToken));
});

app.MapPost("/api/people_map_plus/onedrive/device/start", async (
    OneDriveDeviceAuthService deviceAuthService,
    CancellationToken cancellationToken) =>
{
    var result = await deviceAuthService.StartAsync(cancellationToken);
    var statusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
    return Results.Json(result, statusCode: statusCode);
});

app.MapPost("/api/people_map_plus/onedrive/device/poll", async (
    OneDriveDeviceAuthService deviceAuthService,
    CancellationToken cancellationToken) =>
{
    var result = await deviceAuthService.PollAsync(cancellationToken);
    var statusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
    return Results.Json(result, statusCode: statusCode);
});

app.MapGet("/", () =>
{
    return Results.Content(IngressPage.Html, "text/html");
});

app.Run();
