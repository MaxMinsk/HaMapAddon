using System.Text.Json;

namespace PeopleMapPlus.Addon;

public sealed record DeviceCodeStartResult(
    bool Success,
    string Status,
    string Message,
    string? UserCode,
    string? VerificationUri,
    string? VerificationUriComplete,
    int? ExpiresInSeconds,
    int? IntervalSeconds
);

public sealed record DeviceCodePollResult(
    bool Success,
    string Status,
    string Message
);

public sealed class OneDriveDeviceAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AddonOptionsProvider _optionsProvider;
    private readonly OneDriveTokenStore _tokenStore;
    private readonly ILogger<OneDriveDeviceAuthService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DeviceCodeSession? _session;

    public OneDriveDeviceAuthService(
        IHttpClientFactory httpClientFactory,
        AddonOptionsProvider optionsProvider,
        OneDriveTokenStore tokenStore,
        ILogger<OneDriveDeviceAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsProvider = optionsProvider;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task<object> GetStatusAsync(CancellationToken cancellationToken)
    {
        var options = _optionsProvider.Load();
        var hasStoredToken = await _tokenStore.HasRefreshTokenAsync(cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var session = _session;
            var activeSession = session is DeviceCodeSession value
                ? new
                {
                    userCode = value.UserCode,
                    verificationUri = value.VerificationUri,
                    verificationUriComplete = value.VerificationUriComplete,
                    expiresAtUtc = value.ExpiresAtUtc,
                    intervalSeconds = value.IntervalSeconds,
                    createdAtUtc = value.CreatedAtUtc
                }
                : null;

            return new
            {
                mode = "device_code",
                hasStoredRefreshToken = hasStoredToken,
                hasConfigRefreshToken = !string.IsNullOrWhiteSpace(options.OneDriveRefreshToken),
                activeSession
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<DeviceCodeStartResult> StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var options = _optionsProvider.Load();
            if (string.IsNullOrWhiteSpace(options.OneDriveClientId))
            {
                return new DeviceCodeStartResult(
                    false, "invalid_config", "Set onedrive_client_id in add-on Configuration first.",
                    null, null, null, null, null);
            }

            var endpoint = $"https://login.microsoftonline.com/{options.OneDriveTenant}/oauth2/v2.0/devicecode";
            var payload = new Dictionary<string, string>
            {
                ["client_id"] = options.OneDriveClientId,
                ["scope"] = options.OneDriveScope
            };

            using var client = _httpClientFactory.CreateClient(nameof(OneDriveDeviceAuthService));
            using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(payload), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var details = ExtractErrorDescription(body) ?? $"HTTP {(int)response.StatusCode}";
                _logger.LogWarning("Device code start failed. Status={StatusCode}, Body={Body}", (int)response.StatusCode, Truncate(body, 600));
                return new DeviceCodeStartResult(
                    false, "request_failed", $"Cannot start device auth: {Truncate(details, 220)}",
                    null, null, null, null, null);
            }

            using var document = JsonDocument.Parse(body);
            if (!TryReadSession(document.RootElement, out var session))
            {
                return new DeviceCodeStartResult(
                    false, "invalid_response", "Unexpected device auth response from Microsoft.",
                    null, null, null, null, null);
            }

            await _lock.WaitAsync(cancellationToken);
            try
            {
                _session = session;
            }
            finally
            {
                _lock.Release();
            }

            return new DeviceCodeStartResult(
                true,
                "verification_required",
                session.Message,
                session.UserCode,
                session.VerificationUri,
                session.VerificationUriComplete,
                session.ExpiresInSeconds,
                session.IntervalSeconds
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device code start crashed.");
            return new DeviceCodeStartResult(
                false, "exception", $"Device auth crashed: {ex.Message}",
                null, null, null, null, null);
        }
    }

    public async Task<DeviceCodePollResult> PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            DeviceCodeSession? session;
            var options = _optionsProvider.Load();
            await _lock.WaitAsync(cancellationToken);
            try
            {
                session = _session;
            }
            finally
            {
                _lock.Release();
            }

            if (session is not DeviceCodeSession activeSession)
            {
                return new DeviceCodePollResult(false, "no_session", "No active device auth session. Start auth first.");
            }

            if (DateTimeOffset.UtcNow > activeSession.ExpiresAtUtc)
            {
                await ClearSessionAsync(cancellationToken);
                return new DeviceCodePollResult(false, "expired", "Device auth session expired. Start again.");
            }

            var endpoint = $"https://login.microsoftonline.com/{options.OneDriveTenant}/oauth2/v2.0/token";
            var payload = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = options.OneDriveClientId,
                ["device_code"] = activeSession.DeviceCode
            };
            if (!string.IsNullOrWhiteSpace(options.OneDriveClientSecret))
            {
                payload["client_secret"] = options.OneDriveClientSecret;
            }

            using var client = _httpClientFactory.CreateClient(nameof(OneDriveDeviceAuthService));
            using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(payload), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (response.IsSuccessStatusCode)
            {
                var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement)
                    ? refreshTokenElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    return new DeviceCodePollResult(false, "invalid_response", "Token response has no refresh_token.");
                }

                await _tokenStore.SetRefreshTokenAsync(refreshToken, cancellationToken);
                await ClearSessionAsync(cancellationToken);
                return new DeviceCodePollResult(true, "connected", "OneDrive connected. Refresh token saved.");
            }

            var error = root.TryGetProperty("error", out var err) ? err.GetString() : null;
            var description = root.TryGetProperty("error_description", out var desc) ? desc.GetString() : null;
            return error switch
            {
                "authorization_pending" => new DeviceCodePollResult(true, "pending", "Waiting for confirmation in Microsoft account."),
                "slow_down" => new DeviceCodePollResult(true, "pending", "Too frequent polling. Wait a few seconds and retry."),
                "expired_token" => await ExpiredAsync(cancellationToken),
                _ => new DeviceCodePollResult(false, "token_error", string.IsNullOrWhiteSpace(description) ? "Device auth failed." : Truncate(description!, 300))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device code poll crashed.");
            return new DeviceCodePollResult(false, "exception", $"Device poll crashed: {ex.Message}");
        }
    }

    private async Task<DeviceCodePollResult> ExpiredAsync(CancellationToken cancellationToken)
    {
        await ClearSessionAsync(cancellationToken);
        return new DeviceCodePollResult(false, "expired", "Device auth session expired. Start again.");
    }

    private async Task ClearSessionAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _session = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool TryReadSession(JsonElement root, out DeviceCodeSession session)
    {
        session = default;
        if (!TryGetNonEmptyString(root, "device_code", out var deviceCode)
            || !TryGetNonEmptyString(root, "user_code", out var userCode)
            || !TryGetNonEmptyString(root, "verification_uri", out var verificationUri)
            || !TryGetInt(root, "expires_in", out var expiresIn)
            || !TryGetInt(root, "interval", out var interval))
        {
            return false;
        }

        root.TryGetProperty("verification_uri_complete", out var completeElement);
        var message = root.TryGetProperty("message", out var msgElement) && !string.IsNullOrWhiteSpace(msgElement.GetString())
            ? msgElement.GetString()!
            : $"Open {verificationUri} and enter code {userCode}.";

        session = new DeviceCodeSession(
            DeviceCode: deviceCode,
            UserCode: userCode,
            VerificationUri: verificationUri,
            VerificationUriComplete: completeElement.GetString(),
            Message: message,
            ExpiresInSeconds: expiresIn,
            IntervalSeconds: interval,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(expiresIn)
        );
        return true;
    }

    private static bool TryGetNonEmptyString(JsonElement root, string property, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(property, out var element))
        {
            return false;
        }

        var parsed = element.GetString();
        if (string.IsNullOrWhiteSpace(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetInt(JsonElement root, string property, out int value)
    {
        value = default;
        return root.TryGetProperty(property, out var element) && element.TryGetInt32(out value);
    }

    private static string Truncate(string input, int maxLength)
    {
        if (input.Length <= maxLength)
        {
            return input;
        }

        return input[..maxLength] + "...";
    }

    private static string? ExtractErrorDescription(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error_description", out var descriptionElement))
            {
                return descriptionElement.GetString();
            }

            if (root.TryGetProperty("error", out var errorElement))
            {
                return errorElement.GetString();
            }
        }
        catch
        {
            // ignore parse errors and fallback to raw body
        }

        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    private readonly record struct DeviceCodeSession(
        string DeviceCode,
        string UserCode,
        string VerificationUri,
        string? VerificationUriComplete,
        string Message,
        int ExpiresInSeconds,
        int IntervalSeconds,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc
    );
}
