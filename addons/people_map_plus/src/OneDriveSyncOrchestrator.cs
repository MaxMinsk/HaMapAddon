using System.Net.Http.Headers;
using System.Text.Json;

namespace PeopleMapPlus.Addon;

public sealed record SyncResult(
    bool Success,
    string Status,
    string Message,
    int Examined,
    int Downloaded,
    int Skipped,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc
);

public sealed class OneDriveSyncOrchestrator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".heic",
        ".heif",
        ".webp"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AddonOptionsProvider _optionsProvider;
    private readonly SyncRepository _repository;
    private readonly ILogger<OneDriveSyncOrchestrator> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private SyncResult? _lastResult;

    public OneDriveSyncOrchestrator(
        IHttpClientFactory httpClientFactory,
        AddonOptionsProvider optionsProvider,
        SyncRepository repository,
        ILogger<OneDriveSyncOrchestrator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsProvider = optionsProvider;
        _repository = repository;
        _logger = logger;
    }

    public SyncResult? GetLastResult() => _lastResult;

    public async Task<SyncResult> RunOnceAsync(string reason, CancellationToken cancellationToken)
    {
        var options = _optionsProvider.Load();
        if (!options.OneDriveEnabled)
        {
            return Remember(new SyncResult(
                Success: true,
                Status: "skipped",
                Message: "OneDrive sync is disabled.",
                Examined: 0,
                Downloaded: 0,
                Skipped: 0,
                StartedAtUtc: DateTimeOffset.UtcNow,
                FinishedAtUtc: DateTimeOffset.UtcNow
            ));
        }

        if (string.IsNullOrWhiteSpace(options.OneDriveClientId) || string.IsNullOrWhiteSpace(options.OneDriveRefreshToken))
        {
            return Remember(new SyncResult(
                Success: false,
                Status: "invalid_config",
                Message: "Missing OneDrive client_id or refresh_token.",
                Examined: 0,
                Downloaded: 0,
                Skipped: 0,
                StartedAtUtc: DateTimeOffset.UtcNow,
                FinishedAtUtc: DateTimeOffset.UtcNow
            ));
        }

        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            return Remember(new SyncResult(
                Success: true,
                Status: "busy",
                Message: "Sync already in progress.",
                Examined: 0,
                Downloaded: 0,
                Skipped: 0,
                StartedAtUtc: DateTimeOffset.UtcNow,
                FinishedAtUtc: DateTimeOffset.UtcNow
            ));
        }

        try
        {
            return Remember(await RunInternalAsync(options, reason, cancellationToken));
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<SyncResult> RunInternalAsync(
        NormalizedAddonOptions options,
        string reason,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var cutoffUtc = startedAt.AddDays(-options.LookbackDays);
        _logger.LogInformation(
            "Starting OneDrive sync. Reason={Reason}, LookbackDays={LookbackDays}, Destination=/media/{DestinationSubdir}",
            reason,
            options.LookbackDays,
            options.DestinationSubdir
        );

        string? accessToken;
        try
        {
            accessToken = await RequestAccessTokenAsync(options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to refresh OneDrive access token.");
            return new SyncResult(
                Success: false,
                Status: "auth_error",
                Message: "Unable to refresh OneDrive access token.",
                Examined: 0,
                Downloaded: 0,
                Skipped: 0,
                StartedAtUtc: startedAt,
                FinishedAtUtc: DateTimeOffset.UtcNow
            );
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new SyncResult(
                Success: false,
                Status: "auth_error",
                Message: "Received empty access token from OneDrive.",
                Examined: 0,
                Downloaded: 0,
                Skipped: 0,
                StartedAtUtc: startedAt,
                FinishedAtUtc: DateTimeOffset.UtcNow
            );
        }

        var stateKeyPrefix = BuildStateKeyPrefix(options);
        var deltaLink = _repository.GetState($"{stateKeyPrefix}:delta_link");
        var pageUrl = string.IsNullOrWhiteSpace(deltaLink) ? BuildInitialDeltaUrl(options) : deltaLink;

        var examined = 0;
        var skipped = 0;
        var downloaded = 0;
        string? newestDeltaLink = null;

        while (!string.IsNullOrWhiteSpace(pageUrl) && downloaded < options.MaxFilesPerRun)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var graphClient = _httpClientFactory.CreateClient(nameof(OneDriveSyncOrchestrator));
            using var response = await graphClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Graph delta request failed. Status={StatusCode}, Body={Body}",
                    (int)response.StatusCode,
                    Truncate(body, 400)
                );

                return new SyncResult(
                    Success: false,
                    Status: "graph_error",
                    Message: $"Graph request failed with status {(int)response.StatusCode}.",
                    Examined: examined,
                    Downloaded: downloaded,
                    Skipped: skipped,
                    StartedAtUtc: startedAt,
                    FinishedAtUtc: DateTimeOffset.UtcNow
                );
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    examined++;
                    if (downloaded >= options.MaxFilesPerRun)
                    {
                        break;
                    }

                    if (!TryParseDriveFile(item, out var remoteFile))
                    {
                        skipped++;
                        continue;
                    }

                    if (remoteFile.LastModifiedUtc < cutoffUtc)
                    {
                        skipped++;
                        continue;
                    }

                    if (!IsImage(remoteFile.FileName))
                    {
                        skipped++;
                        continue;
                    }

                    var existing = _repository.GetFileRecord(remoteFile.ItemId);
                    if (existing is not null
                        && string.Equals(existing.ETag, remoteFile.ETag, StringComparison.Ordinal)
                        && File.Exists(existing.LocalPath))
                    {
                        skipped++;
                        continue;
                    }

                    var localPath = BuildLocalPath(options.DestinationSubdir, remoteFile);
                    try
                    {
                        await DownloadFileAsync(remoteFile.DownloadUrl, localPath, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed downloading OneDrive file {ItemId} ({FileName}).",
                            remoteFile.ItemId,
                            remoteFile.FileName
                        );
                        skipped++;
                        continue;
                    }

                    _repository.UpsertFile(new DownloadedFileRecord(
                        ItemId: remoteFile.ItemId,
                        ETag: remoteFile.ETag,
                        SizeBytes: remoteFile.SizeBytes,
                        LastModifiedUtc: remoteFile.LastModifiedUtc,
                        LocalPath: localPath
                    ));
                    downloaded++;
                }
            }

            if (root.TryGetProperty("@odata.deltaLink", out var deltaLinkElement))
            {
                newestDeltaLink = deltaLinkElement.GetString();
                pageUrl = null;
            }
            else if (root.TryGetProperty("@odata.nextLink", out var nextLinkElement))
            {
                pageUrl = nextLinkElement.GetString();
            }
            else
            {
                pageUrl = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(newestDeltaLink))
        {
            _repository.SetState($"{stateKeyPrefix}:delta_link", newestDeltaLink);
        }

        _repository.SetState($"{stateKeyPrefix}:last_sync_utc", DateTimeOffset.UtcNow.ToString("O"));

        _logger.LogInformation(
            "OneDrive sync finished. Examined={Examined}, Downloaded={Downloaded}, Skipped={Skipped}",
            examined,
            downloaded,
            skipped
        );

        return new SyncResult(
            Success: true,
            Status: "ok",
            Message: "Sync completed.",
            Examined: examined,
            Downloaded: downloaded,
            Skipped: skipped,
            StartedAtUtc: startedAt,
            FinishedAtUtc: DateTimeOffset.UtcNow
        );
    }

    private async Task<string?> RequestAccessTokenAsync(NormalizedAddonOptions options, CancellationToken cancellationToken)
    {
        var tokenEndpoint = $"https://login.microsoftonline.com/{options.OneDriveTenant}/oauth2/v2.0/token";
        var payload = new Dictionary<string, string>
        {
            ["client_id"] = options.OneDriveClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = options.OneDriveRefreshToken,
            ["scope"] = options.OneDriveScope
        };
        if (!string.IsNullOrWhiteSpace(options.OneDriveClientSecret))
        {
            payload["client_secret"] = options.OneDriveClientSecret;
        }

        using var client = _httpClientFactory.CreateClient(nameof(OneDriveSyncOrchestrator));
        using var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(payload), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Token refresh failed. Status={StatusCode}, Body={Body}",
                (int)response.StatusCode,
                Truncate(body, 300)
            );
            return null;
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("access_token", out var accessToken))
        {
            return null;
        }

        return accessToken.GetString();
    }

    private static string BuildInitialDeltaUrl(NormalizedAddonOptions options)
    {
        var driveResource = string.IsNullOrWhiteSpace(options.OneDriveDriveId)
            ? "me/drive"
            : $"drives/{Uri.EscapeDataString(options.OneDriveDriveId)}";

        if (options.OneDriveFolderPath == "/")
        {
            return $"https://graph.microsoft.com/v1.0/{driveResource}/root/delta?$top=200";
        }

        return $"https://graph.microsoft.com/v1.0/{driveResource}/root:{EncodeDrivePath(options.OneDriveFolderPath)}:/delta?$top=200";
    }

    private static string BuildStateKeyPrefix(NormalizedAddonOptions options)
    {
        var drive = string.IsNullOrWhiteSpace(options.OneDriveDriveId) ? "me" : options.OneDriveDriveId;
        return $"onedrive:{drive}:{options.OneDriveFolderPath}";
    }

    private static string EncodeDrivePath(string normalizedPath)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    private static bool TryParseDriveFile(JsonElement element, out RemoteDriveFile file)
    {
        file = default;

        if (element.TryGetProperty("deleted", out _))
        {
            return false;
        }

        if (!element.TryGetProperty("file", out _))
        {
            return false;
        }

        if (!element.TryGetProperty("id", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            return false;
        }

        if (!element.TryGetProperty("name", out var nameElement) || string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            return false;
        }

        if (!element.TryGetProperty("@microsoft.graph.downloadUrl", out var downloadUrlElement)
            || string.IsNullOrWhiteSpace(downloadUrlElement.GetString()))
        {
            return false;
        }

        var lastModifiedUtc = DateTimeOffset.UtcNow;
        if (element.TryGetProperty("lastModifiedDateTime", out var modifiedElement)
            && DateTimeOffset.TryParse(modifiedElement.GetString(), out var parsedModified))
        {
            lastModifiedUtc = parsedModified.ToUniversalTime();
        }

        long? sizeBytes = null;
        if (element.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize))
        {
            sizeBytes = parsedSize;
        }

        file = new RemoteDriveFile(
            ItemId: idElement.GetString()!,
            FileName: nameElement.GetString()!,
            DownloadUrl: downloadUrlElement.GetString()!,
            ETag: element.TryGetProperty("eTag", out var etagElement) ? etagElement.GetString() : null,
            SizeBytes: sizeBytes,
            LastModifiedUtc: lastModifiedUtc
        );
        return true;
    }

    private static bool IsImage(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return SupportedExtensions.Contains(extension);
    }

    private static string BuildLocalPath(string destinationSubdir, RemoteDriveFile file)
    {
        var timestamp = file.LastModifiedUtc;
        var subFolder = Path.Combine(
            "/media",
            destinationSubdir.Replace('/', Path.DirectorySeparatorChar),
            timestamp.ToString("yyyy"),
            timestamp.ToString("MM")
        );
        Directory.CreateDirectory(subFolder);

        var extension = Path.GetExtension(file.FileName);
        var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(file.FileName));
        if (safeName.Length > 80)
        {
            safeName = safeName[..80];
        }

        var baseName = $"{timestamp:yyyyMMdd_HHmmss}_{file.ItemId[..Math.Min(file.ItemId.Length, 8)]}_{safeName}{extension}";
        var path = Path.Combine(subFolder, baseName);
        var counter = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(subFolder, $"{Path.GetFileNameWithoutExtension(baseName)}_{counter}{extension}");
            counter++;
        }

        return path;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value
            .Where(ch => !invalid.Contains(ch))
            .Select(ch => char.IsWhiteSpace(ch) ? '_' : ch)
            .ToArray();
        var normalized = new string(chars).Trim('_', '.');
        return string.IsNullOrWhiteSpace(normalized) ? "photo" : normalized;
    }

    private async Task DownloadFileAsync(string sourceUrl, string destinationPath, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(nameof(OneDriveSyncOrchestrator));
        using var response = await client.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tempPath = destinationPath + ".tmp";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(tempPath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
    }

    private static string Truncate(string input, int maxLength)
    {
        if (input.Length <= maxLength)
        {
            return input;
        }

        return input[..maxLength] + "...";
    }

    private SyncResult Remember(SyncResult result)
    {
        _lastResult = result;
        return result;
    }

    private readonly record struct RemoteDriveFile(
        string ItemId,
        string FileName,
        string DownloadUrl,
        string? ETag,
        long? SizeBytes,
        DateTimeOffset LastModifiedUtc
    );
}

