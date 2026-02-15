using System.Text.Json.Serialization;

namespace PeopleMapPlus.Addon;

public sealed class AddonOptions
{
    [JsonPropertyName("log_level")]
    public string? LogLevel { get; init; } = "info";

    [JsonPropertyName("onedrive_enabled")]
    public bool? OneDriveEnabled { get; init; } = false;

    [JsonPropertyName("onedrive_tenant")]
    public string? OneDriveTenant { get; init; } = "consumers";

    [JsonPropertyName("onedrive_client_id")]
    public string? OneDriveClientId { get; init; } = string.Empty;

    [JsonPropertyName("onedrive_client_secret")]
    public string? OneDriveClientSecret { get; init; } = string.Empty;

    [JsonPropertyName("onedrive_refresh_token")]
    public string? OneDriveRefreshToken { get; init; } = string.Empty;

    [JsonPropertyName("onedrive_scope")]
    public string? OneDriveScope { get; init; } = "offline_access Files.Read User.Read";

    [JsonPropertyName("onedrive_drive_id")]
    public string? OneDriveDriveId { get; init; } = string.Empty;

    [JsonPropertyName("onedrive_folder_path")]
    public string? OneDriveFolderPath { get; init; } = "/";

    [JsonPropertyName("destination_subdir")]
    public string? DestinationSubdir { get; init; } = "people_map_plus/onedrive";

    [JsonPropertyName("lookback_days")]
    public int? LookbackDays { get; init; } = 5;

    [JsonPropertyName("sync_interval_hours")]
    public int? SyncIntervalHours { get; init; } = 24;

    [JsonPropertyName("max_files_per_run")]
    public int? MaxFilesPerRun { get; init; } = 500;

    [JsonPropertyName("run_sync_on_startup")]
    public bool? RunSyncOnStartup { get; init; } = true;
}

public sealed record NormalizedAddonOptions(
    string LogLevel,
    bool OneDriveEnabled,
    string OneDriveTenant,
    string OneDriveClientId,
    string OneDriveClientSecret,
    string OneDriveRefreshToken,
    string OneDriveScope,
    string OneDriveDriveId,
    string OneDriveFolderPath,
    string DestinationSubdir,
    int LookbackDays,
    int SyncIntervalHours,
    int MaxFilesPerRun,
    bool RunSyncOnStartup
);

