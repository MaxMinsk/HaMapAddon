using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PeopleMapPlus.Addon;

public sealed record TrackPoint(
    double Lat,
    double Lon,
    DateTimeOffset Ts,
    int? Accuracy,
    string? State
);

public sealed record EntityTrack(
    string EntityId,
    IReadOnlyList<TrackPoint> Points
);

public sealed record TracksQueryResult(
    bool Success,
    string Status,
    string Message,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<EntityTrack> Tracks,
    int TotalPoints
);

public sealed class HomeAssistantHistoryService
{
    private const string SupervisorHistoryBase = "http://supervisor/core/api/history/period/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HomeAssistantHistoryService> _logger;

    public HomeAssistantHistoryService(
        IHttpClientFactory httpClientFactory,
        ILogger<HomeAssistantHistoryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<TracksQueryResult> QueryTracksAsync(
        IReadOnlyList<string> entities,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int maxPointsPerEntity,
        double minDistanceMeters,
        CancellationToken cancellationToken)
    {
        var normalizedEntities = entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity))
            .Select(entity => entity.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedEntities.Length == 0)
        {
            return new TracksQueryResult(
                Success: false,
                Status: "invalid_entities",
                Message: "No entities provided.",
                FromUtc: fromUtc,
                ToUtc: toUtc,
                Tracks: [],
                TotalPoints: 0
            );
        }

        var token = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            return new TracksQueryResult(
                Success: false,
                Status: "missing_supervisor_token",
                Message: "SUPERVISOR_TOKEN is not available in add-on environment.",
                FromUtc: fromUtc,
                ToUtc: toUtc,
                Tracks: [],
                TotalPoints: 0
            );
        }

        var requestUrl = BuildHistoryUrl(normalizedEntities, fromUtc, toUtc);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var client = _httpClientFactory.CreateClient(nameof(HomeAssistantHistoryService));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "History request failed. Status={StatusCode}, Url={Url}, Body={Body}",
                    (int)response.StatusCode,
                    requestUrl,
                    Truncate(body, 450)
                );
                return new TracksQueryResult(
                    Success: false,
                    Status: "history_error",
                    Message: $"Home Assistant history API failed with status {(int)response.StatusCode}.",
                    FromUtc: fromUtc,
                    ToUtc: toUtc,
                    Tracks: [],
                    TotalPoints: 0
                );
            }

            var tracks = ParseTracks(body, maxPointsPerEntity, minDistanceMeters);
            return new TracksQueryResult(
                Success: true,
                Status: "ok",
                Message: $"Loaded tracks for {tracks.Count} entities.",
                FromUtc: fromUtc,
                ToUtc: toUtc,
                Tracks: tracks,
                TotalPoints: tracks.Sum(track => track.Points.Count)
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Track history query failed.");
            return new TracksQueryResult(
                Success: false,
                Status: "exception",
                Message: $"History query failed: {ex.Message}",
                FromUtc: fromUtc,
                ToUtc: toUtc,
                Tracks: [],
                TotalPoints: 0
            );
        }
    }

    private static string BuildHistoryUrl(
        IReadOnlyList<string> entities,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        var fromPart = Uri.EscapeDataString(fromUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        var toPart = Uri.EscapeDataString(toUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        var entityPart = Uri.EscapeDataString(string.Join(",", entities));
        return $"{SupervisorHistoryBase}{fromPart}?end_time={toPart}&filter_entity_id={entityPart}&significant_changes_only=0";
    }

    private static IReadOnlyList<EntityTrack> ParseTracks(string body, int maxPointsPerEntity, double minDistanceMeters)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var byEntity = new Dictionary<string, List<TrackPoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityBucket in document.RootElement.EnumerateArray())
        {
            if (entityBucket.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var state in entityBucket.EnumerateArray())
            {
                if (state.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!state.TryGetProperty("entity_id", out var entityIdElement))
                {
                    continue;
                }

                var entityId = entityIdElement.GetString();
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    continue;
                }

                if (!state.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var lat = TryReadDouble(attributes, "latitude");
                var lon = TryReadDouble(attributes, "longitude");
                if (lat is null || lon is null)
                {
                    continue;
                }

                var ts = TryReadDateTimeOffset(state, "last_updated")
                    ?? TryReadDateTimeOffset(state, "last_changed");
                if (ts is null)
                {
                    continue;
                }

                var accuracy = TryReadInt(attributes, "gps_accuracy");
                var status = state.TryGetProperty("state", out var stateElement)
                    ? stateElement.GetString()
                    : null;

                if (!byEntity.TryGetValue(entityId, out var points))
                {
                    points = [];
                    byEntity[entityId] = points;
                }

                points.Add(new TrackPoint(
                    Lat: lat.Value,
                    Lon: lon.Value,
                    Ts: ts.Value.ToUniversalTime(),
                    Accuracy: accuracy,
                    State: status
                ));
            }
        }

        return byEntity
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new EntityTrack(
                EntityId: item.Key,
                Points: SimplifyPoints(item.Value, maxPointsPerEntity, minDistanceMeters)
            ))
            .Where(track => track.Points.Count > 0)
            .ToArray();
    }

    private static IReadOnlyList<TrackPoint> SimplifyPoints(
        IReadOnlyList<TrackPoint> rawPoints,
        int maxPointsPerEntity,
        double minDistanceMeters)
    {
        if (rawPoints.Count == 0)
        {
            return [];
        }

        var ordered = rawPoints
            .OrderBy(point => point.Ts)
            .ToArray();

        var deduped = new List<TrackPoint>(ordered.Length);
        TrackPoint? last = null;
        foreach (var point in ordered)
        {
            if (last is null)
            {
                deduped.Add(point);
                last = point;
                continue;
            }

            var distance = HaversineMeters(last.Lat, last.Lon, point.Lat, point.Lon);
            var sameCoordinates = distance < 0.5;
            var sameTime = Math.Abs((point.Ts - last.Ts).TotalSeconds) < 1;
            if (sameCoordinates && sameTime)
            {
                continue;
            }

            if (distance < minDistanceMeters)
            {
                continue;
            }

            deduped.Add(point);
            last = point;
        }

        if (deduped.Count <= maxPointsPerEntity || maxPointsPerEntity <= 1)
        {
            return deduped;
        }

        var sampled = new List<TrackPoint>(maxPointsPerEntity);
        var step = (double)(deduped.Count - 1) / (maxPointsPerEntity - 1);
        var usedIndexes = new HashSet<int>();
        for (var i = 0; i < maxPointsPerEntity; i++)
        {
            var index = (int)Math.Round(i * step);
            index = Math.Clamp(index, 0, deduped.Count - 1);
            if (usedIndexes.Add(index))
            {
                sampled.Add(deduped[index]);
            }
        }

        if (!sampled.Any() || sampled[^1].Ts != deduped[^1].Ts)
        {
            sampled.Add(deduped[^1]);
        }

        return sampled
            .OrderBy(point => point.Ts)
            .ToArray();
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusM = 6371000;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusM * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180);

    private static DateTimeOffset? TryReadDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out var parsed) ? parsed : null;
    }

    private static int? TryReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var parsedInt))
        {
            return parsedInt;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out parsedInt))
        {
            return parsedInt;
        }

        return null;
    }

    private static double? TryReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var parsedDouble))
        {
            return parsedDouble;
        }

        if (property.ValueKind == JsonValueKind.String
            && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsedDouble))
        {
            return parsedDouble;
        }

        return null;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }
}
