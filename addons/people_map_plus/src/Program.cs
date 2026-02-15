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
builder.Services.AddHttpClient(nameof(HomeAssistantHistoryService), client =>
{
    client.Timeout = TimeSpan.FromSeconds(180);
});
builder.Services.AddSingleton<AddonOptionsProvider>();
builder.Services.AddSingleton<OneDriveTokenStore>();
builder.Services.AddSingleton<SyncRepository>();
builder.Services.AddSingleton<OneDriveSyncOrchestrator>();
builder.Services.AddSingleton<OneDriveDeviceAuthService>();
builder.Services.AddSingleton<HomeAssistantHistoryService>();
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
        version = "0.1.15",
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

app.MapGet("/api/people_map_plus/photos", (
    int? limit,
    int? hours,
    string? fromUtc,
    string? toUtc,
    double? minLat,
    double? maxLat,
    double? minLon,
    double? maxLon,
    SyncRepository repository) =>
{
    if (!TryParseOptionalDateTimeOffset(fromUtc, out var parsedFromUtc))
    {
        return Results.Json(new
        {
            success = false,
            status = "invalid_from_utc",
            message = "fromUtc must be ISO-8601 timestamp."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (!TryParseOptionalDateTimeOffset(toUtc, out var parsedToUtc))
    {
        return Results.Json(new
        {
            success = false,
            status = "invalid_to_utc",
            message = "toUtc must be ISO-8601 timestamp."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (hours is not null && parsedFromUtc is null)
    {
        parsedFromUtc = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours.Value, 1, 24 * 30));
    }

    var hasAnyBboxParam = minLat is not null || maxLat is not null || minLon is not null || maxLon is not null;
    var hasFullBbox = minLat is not null && maxLat is not null && minLon is not null && maxLon is not null;
    if (hasAnyBboxParam && !hasFullBbox)
    {
        return Results.Json(new
        {
            success = false,
            status = "invalid_bbox",
            message = "Provide all bbox params: minLat,maxLat,minLon,maxLon."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    var take = Math.Clamp(limit ?? 50, 1, 500);
    var rows = repository.GetGeotaggedPhotos(
        take,
        parsedFromUtc,
        parsedToUtc,
        minLat,
        maxLat,
        minLon,
        maxLon);

    var items = rows
        .Where(row => File.Exists(row.LocalPath))
        .Select(row => new
        {
            itemId = row.ItemId,
            capturedAtUtc = row.CaptureUtc,
            lat = row.Latitude,
            lon = row.Longitude,
            width = row.WidthPx,
            height = row.HeightPx,
            localPath = row.LocalPath,
            thumbnailPath = row.ThumbnailPath,
            mediaUrl = ToMediaLocalUrl(row.LocalPath),
            thumbnailUrl = row.ThumbnailPath is null ? null : ToMediaLocalUrl(row.ThumbnailPath),
            indexedAtUtc = row.IndexedAtUtc
        })
        .ToArray();

    return Results.Ok(new
    {
        success = true,
        count = items.Length,
        filters = new
        {
            limit = take,
            fromUtc = parsedFromUtc,
            toUtc = parsedToUtc,
            minLat,
            maxLat,
            minLon,
            maxLon
        },
        items
    });
});

app.MapGet("/api/people_map_plus/tracks", async (
    string? entities,
    int? days,
    string? fromUtc,
    string? toUtc,
    int? maxPoints,
    double? minDistanceMeters,
    HomeAssistantHistoryService historyService,
    CancellationToken cancellationToken) =>
{
    if (!TryParseOptionalDateTimeOffset(fromUtc, out var parsedFromUtc))
    {
        return Results.Json(new
        {
            success = false,
            status = "invalid_from_utc",
            message = "fromUtc must be ISO-8601 timestamp."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (!TryParseOptionalDateTimeOffset(toUtc, out var parsedToUtc))
    {
        return Results.Json(new
        {
            success = false,
            status = "invalid_to_utc",
            message = "toUtc must be ISO-8601 timestamp."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    var to = parsedToUtc ?? DateTimeOffset.UtcNow;
    var periodDays = Math.Clamp(days ?? 1, 1, 30);
    var from = parsedFromUtc ?? to.AddDays(-periodDays);
    if (from >= to)
    {
        return Results.Json(new
        {
            success = false,
            status = "invalid_range",
            message = "fromUtc must be earlier than toUtc."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    var entityList = (entities ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (entityList.Length == 0)
    {
        return Results.Json(new
        {
            success = false,
            status = "invalid_entities",
            message = "Provide entities as comma-separated list, for example entities=person.max,person.maria."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    var result = await historyService.QueryTracksAsync(
        entityList,
        from.ToUniversalTime(),
        to.ToUniversalTime(),
        maxPointsPerEntity: Math.Clamp(maxPoints ?? 500, 50, 5000),
        minDistanceMeters: Math.Clamp(minDistanceMeters ?? 0, 0, 2000),
        cancellationToken);

    var statusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;
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

static bool TryParseOptionalDateTimeOffset(string? value, out DateTimeOffset? parsed)
{
    parsed = null;
    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    if (!DateTimeOffset.TryParse(value, out var parsedValue))
    {
        return false;
    }

    parsed = parsedValue.ToUniversalTime();
    return true;
}

static string? ToMediaLocalUrl(string? absolutePath)
{
    if (string.IsNullOrWhiteSpace(absolutePath))
    {
        return null;
    }

    const string mediaPrefix = "/media/";
    if (!absolutePath.StartsWith(mediaPrefix, StringComparison.Ordinal))
    {
        return null;
    }

    var relative = absolutePath[mediaPrefix.Length..].Replace('\\', '/');
    return "/media/local/" + relative;
}
