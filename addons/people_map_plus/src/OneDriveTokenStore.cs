using System.Text.Json;

namespace PeopleMapPlus.Addon;

public sealed class OneDriveTokenStore
{
    private const string FilePath = "/data/onedrive_tokens.json";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<OneDriveTokenStore> _logger;

    public OneDriveTokenStore(ILogger<OneDriveTokenStore> logger)
    {
        _logger = logger;
    }

    public async Task<string?> GetRefreshTokenAsync(CancellationToken cancellationToken)
    {
        var snapshot = await ReadAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(snapshot.RefreshToken) ? null : snapshot.RefreshToken;
    }

    public async Task<bool> HasRefreshTokenAsync(CancellationToken cancellationToken)
    {
        return !string.IsNullOrWhiteSpace(await GetRefreshTokenAsync(cancellationToken));
    }

    public async Task SetRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory("/data");
            var snapshot = await ReadCoreAsync(cancellationToken);
            var updated = snapshot with
            {
                RefreshToken = refreshToken.Trim(),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            var json = JsonSerializer.Serialize(updated, JsonOptions);
            await File.WriteAllTextAsync(FilePath, json, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<TokenSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await ReadCoreAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<TokenSnapshot> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return new TokenSnapshot();
        }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var snapshot = await JsonSerializer.DeserializeAsync<TokenSnapshot>(stream, JsonOptions, cancellationToken);
            return snapshot ?? new TokenSnapshot();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read OneDrive token snapshot. Ignoring invalid file.");
            return new TokenSnapshot();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private sealed record TokenSnapshot
    {
        public string? RefreshToken { get; init; } = null;
        public DateTimeOffset? UpdatedAtUtc { get; init; } = null;
    }
}

