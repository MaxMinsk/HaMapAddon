using System.Text.Json;

namespace PeopleMapPlus.Addon;

public sealed class AddonOptionsProvider
{
    private const string OptionsPath = "/data/options.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<AddonOptionsProvider> _logger;

    public AddonOptionsProvider(ILogger<AddonOptionsProvider> logger)
    {
        _logger = logger;
    }

    public NormalizedAddonOptions Load()
    {
        AddonOptions? raw = null;
        if (File.Exists(OptionsPath))
        {
            try
            {
                raw = JsonSerializer.Deserialize<AddonOptions>(File.ReadAllText(OptionsPath), JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse {OptionsPath}. Using defaults.", OptionsPath);
            }
        }
        else
        {
            _logger.LogInformation("{OptionsPath} not found. Using defaults.", OptionsPath);
        }

        raw ??= new AddonOptions();

        return new NormalizedAddonOptions(
            LogLevel: NormalizeLogLevel(raw.LogLevel),
            OneDriveEnabled: raw.OneDriveEnabled ?? false,
            OneDriveTenant: NormalizeString(raw.OneDriveTenant, "consumers"),
            OneDriveClientId: NormalizeString(raw.OneDriveClientId, string.Empty),
            OneDriveClientSecret: NormalizeString(raw.OneDriveClientSecret, string.Empty),
            OneDriveRefreshToken: NormalizeString(raw.OneDriveRefreshToken, string.Empty),
            OneDriveScope: NormalizeString(raw.OneDriveScope, "offline_access Files.Read User.Read"),
            OneDriveDriveId: NormalizeString(raw.OneDriveDriveId, string.Empty),
            OneDriveFolderPath: NormalizeFolderPath(raw.OneDriveFolderPath),
            DestinationSubdir: NormalizeDestinationSubdir(raw.DestinationSubdir),
            LookbackDays: Clamp(raw.LookbackDays ?? 5, 1, 30),
            SyncIntervalHours: Clamp(raw.SyncIntervalHours ?? 24, 1, 168),
            MaxFilesPerRun: Clamp(raw.MaxFilesPerRun ?? 500, 1, 2000),
            MaxSize: Clamp(raw.MaxSize ?? 2500, 512, 10000),
            RunSyncOnStartup: raw.RunSyncOnStartup ?? true
        );
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    private static string NormalizeString(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
    }

    private static string NormalizeLogLevel(string? value)
    {
        var normalized = NormalizeString(value, "info").ToLowerInvariant();
        return normalized switch
        {
            "trace" or "debug" or "info" or "notice" or "warning" or "error" or "fatal" => normalized,
            _ => "info"
        };
    }

    private static string NormalizeFolderPath(string? value)
    {
        var normalized = NormalizeString(value, "/");
        if (normalized == "/" || normalized == string.Empty)
        {
            return "/";
        }

        normalized = normalized.Replace('\\', '/');
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        while (normalized.EndsWith('/') && normalized.Length > 1)
        {
            normalized = normalized[..^1];
        }

        return normalized;
    }

    private static string NormalizeDestinationSubdir(string? value)
    {
        var normalized = NormalizeString(value, "people_map_plus/onedrive").Replace('\\', '/');
        var parts = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizePathSegment)
            .Where(part => part.Length > 0)
            .ToArray();

        return parts.Length == 0 ? "people_map_plus/onedrive" : string.Join('/', parts);
    }

    private static string SanitizePathSegment(string segment)
    {
        var chars = segment
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            .ToArray();
        return new string(chars);
    }
}
