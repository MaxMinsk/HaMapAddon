using Microsoft.Data.Sqlite;

namespace PeopleMapPlus.Addon;

public sealed record DownloadedFileRecord(
    string ItemId,
    string? ETag,
    long? SizeBytes,
    DateTimeOffset? LastModifiedUtc,
    string LocalPath
);

public sealed record PhotoIndexRecord(
    string ItemId,
    string? ETag,
    string LocalPath,
    string? ThumbnailPath,
    DateTimeOffset? CaptureUtc,
    double? Latitude,
    double? Longitude,
    int? WidthPx,
    int? HeightPx,
    bool HasGps,
    DateTimeOffset? SourceLastModifiedUtc,
    DateTimeOffset IndexedAtUtc
);

public sealed class SyncRepository
{
    private const string DbPath = "/data/people_map_plus_sync.db";
    private readonly object _syncRoot = new();
    private readonly ILogger<SyncRepository> _logger;

    public SyncRepository(ILogger<SyncRepository> logger)
    {
        _logger = logger;
        EnsureSchema();
    }

    public string? GetState(string key)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM sync_state WHERE key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar()?.ToString();
        }
    }

    public void SetState(string key, string value)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sync_state (key, value, updated_at_utc)
                VALUES ($key, $value, $updated)
                ON CONFLICT(key) DO UPDATE SET
                  value = excluded.value,
                  updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public DownloadedFileRecord? GetFileRecord(string itemId)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT item_id, e_tag, size_bytes, last_modified_utc, local_path
                FROM downloaded_files
                WHERE item_id = $item_id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$item_id", itemId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var eTag = reader.IsDBNull(1) ? null : reader.GetString(1);
            var size = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2);
            DateTimeOffset? lastModified = null;
            if (!reader.IsDBNull(3) && DateTimeOffset.TryParse(reader.GetString(3), out var parsed))
            {
                lastModified = parsed;
            }

            return new DownloadedFileRecord(
                ItemId: reader.GetString(0),
                ETag: eTag,
                SizeBytes: size,
                LastModifiedUtc: lastModified,
                LocalPath: reader.GetString(4)
            );
        }
    }

    public void UpsertFile(DownloadedFileRecord record)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO downloaded_files
                  (item_id, e_tag, size_bytes, last_modified_utc, local_path, updated_at_utc)
                VALUES
                  ($item_id, $e_tag, $size_bytes, $last_modified_utc, $local_path, $updated_at_utc)
                ON CONFLICT(item_id) DO UPDATE SET
                  e_tag = excluded.e_tag,
                  size_bytes = excluded.size_bytes,
                  last_modified_utc = excluded.last_modified_utc,
                  local_path = excluded.local_path,
                  updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$item_id", record.ItemId);
            command.Parameters.AddWithValue("$e_tag", (object?)record.ETag ?? DBNull.Value);
            command.Parameters.AddWithValue("$size_bytes", (object?)record.SizeBytes ?? DBNull.Value);
            command.Parameters.AddWithValue("$last_modified_utc", (object?)record.LastModifiedUtc?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$local_path", record.LocalPath);
            command.Parameters.AddWithValue("$updated_at_utc", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public PhotoIndexRecord? GetPhotoIndex(string itemId)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT item_id, e_tag, local_path, thumbnail_path, capture_utc, latitude, longitude,
                       width_px, height_px, has_gps, source_last_modified_utc, indexed_at_utc
                FROM photo_index
                WHERE item_id = $item_id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$item_id", itemId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new PhotoIndexRecord(
                ItemId: reader.GetString(0),
                ETag: reader.IsDBNull(1) ? null : reader.GetString(1),
                LocalPath: reader.GetString(2),
                ThumbnailPath: reader.IsDBNull(3) ? null : reader.GetString(3),
                CaptureUtc: ReadDateTimeOffset(reader, 4),
                Latitude: reader.IsDBNull(5) ? null : reader.GetDouble(5),
                Longitude: reader.IsDBNull(6) ? null : reader.GetDouble(6),
                WidthPx: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                HeightPx: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                HasGps: !reader.IsDBNull(9) && reader.GetInt64(9) == 1,
                SourceLastModifiedUtc: ReadDateTimeOffset(reader, 10),
                IndexedAtUtc: ReadDateTimeOffset(reader, 11) ?? DateTimeOffset.UtcNow
            );
        }
    }

    public void UpsertPhotoIndex(PhotoIndexRecord record)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO photo_index
                  (item_id, e_tag, local_path, thumbnail_path, capture_utc, latitude, longitude, width_px, height_px,
                   has_gps, source_last_modified_utc, indexed_at_utc)
                VALUES
                  ($item_id, $e_tag, $local_path, $thumbnail_path, $capture_utc, $latitude, $longitude, $width_px, $height_px,
                   $has_gps, $source_last_modified_utc, $indexed_at_utc)
                ON CONFLICT(item_id) DO UPDATE SET
                  e_tag = excluded.e_tag,
                  local_path = excluded.local_path,
                  thumbnail_path = excluded.thumbnail_path,
                  capture_utc = excluded.capture_utc,
                  latitude = excluded.latitude,
                  longitude = excluded.longitude,
                  width_px = excluded.width_px,
                  height_px = excluded.height_px,
                  has_gps = excluded.has_gps,
                  source_last_modified_utc = excluded.source_last_modified_utc,
                  indexed_at_utc = excluded.indexed_at_utc;
                """;
            command.Parameters.AddWithValue("$item_id", record.ItemId);
            command.Parameters.AddWithValue("$e_tag", (object?)record.ETag ?? DBNull.Value);
            command.Parameters.AddWithValue("$local_path", record.LocalPath);
            command.Parameters.AddWithValue("$thumbnail_path", (object?)record.ThumbnailPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$capture_utc", (object?)record.CaptureUtc?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$latitude", (object?)record.Latitude ?? DBNull.Value);
            command.Parameters.AddWithValue("$longitude", (object?)record.Longitude ?? DBNull.Value);
            command.Parameters.AddWithValue("$width_px", (object?)record.WidthPx ?? DBNull.Value);
            command.Parameters.AddWithValue("$height_px", (object?)record.HeightPx ?? DBNull.Value);
            command.Parameters.AddWithValue("$has_gps", record.HasGps ? 1 : 0);
            command.Parameters.AddWithValue("$source_last_modified_utc", (object?)record.SourceLastModifiedUtc?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$indexed_at_utc", record.IndexedAtUtc.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<PhotoIndexRecord> GetGeotaggedPhotos(
        int limit,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        double? minLat,
        double? maxLat,
        double? minLon,
        double? maxLon)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            var whereParts = new List<string> { "has_gps = 1" };

            if (fromUtc is not null)
            {
                whereParts.Add("capture_utc >= $from_utc");
                command.Parameters.AddWithValue("$from_utc", fromUtc.Value.ToString("O"));
            }

            if (toUtc is not null)
            {
                whereParts.Add("capture_utc <= $to_utc");
                command.Parameters.AddWithValue("$to_utc", toUtc.Value.ToString("O"));
            }

            if (minLat is not null && maxLat is not null)
            {
                whereParts.Add("latitude >= $min_lat");
                whereParts.Add("latitude <= $max_lat");
                command.Parameters.AddWithValue("$min_lat", minLat.Value);
                command.Parameters.AddWithValue("$max_lat", maxLat.Value);
            }

            if (minLon is not null && maxLon is not null)
            {
                whereParts.Add("longitude >= $min_lon");
                whereParts.Add("longitude <= $max_lon");
                command.Parameters.AddWithValue("$min_lon", minLon.Value);
                command.Parameters.AddWithValue("$max_lon", maxLon.Value);
            }

            command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
            command.CommandText = $"""
                SELECT item_id, e_tag, local_path, thumbnail_path, capture_utc, latitude, longitude,
                       width_px, height_px, has_gps, source_last_modified_utc, indexed_at_utc
                FROM photo_index
                WHERE {string.Join(" AND ", whereParts)}
                ORDER BY capture_utc DESC, indexed_at_utc DESC
                LIMIT $limit;
                """;

            var results = new List<PhotoIndexRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new PhotoIndexRecord(
                    ItemId: reader.GetString(0),
                    ETag: reader.IsDBNull(1) ? null : reader.GetString(1),
                    LocalPath: reader.GetString(2),
                    ThumbnailPath: reader.IsDBNull(3) ? null : reader.GetString(3),
                    CaptureUtc: ReadDateTimeOffset(reader, 4),
                    Latitude: reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    Longitude: reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    WidthPx: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    HeightPx: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    HasGps: !reader.IsDBNull(9) && reader.GetInt64(9) == 1,
                    SourceLastModifiedUtc: ReadDateTimeOffset(reader, 10),
                    IndexedAtUtc: ReadDateTimeOffset(reader, 11) ?? DateTimeOffset.UtcNow
                ));
            }

            return results;
        }
    }

    private void EnsureSchema()
    {
        try
        {
            Directory.CreateDirectory("/data");
            lock (_syncRoot)
            {
                using var connection = CreateConnection();
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS sync_state (
                      key TEXT PRIMARY KEY,
                      value TEXT NOT NULL,
                      updated_at_utc TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS downloaded_files (
                      item_id TEXT PRIMARY KEY,
                      e_tag TEXT NULL,
                      size_bytes INTEGER NULL,
                      last_modified_utc TEXT NULL,
                      local_path TEXT NOT NULL,
                      updated_at_utc TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS photo_index (
                      item_id TEXT PRIMARY KEY,
                      e_tag TEXT NULL,
                      local_path TEXT NOT NULL,
                      thumbnail_path TEXT NULL,
                      capture_utc TEXT NULL,
                      latitude REAL NULL,
                      longitude REAL NULL,
                      width_px INTEGER NULL,
                      height_px INTEGER NULL,
                      has_gps INTEGER NOT NULL,
                      source_last_modified_utc TEXT NULL,
                      indexed_at_utc TEXT NOT NULL
                    );

                    CREATE INDEX IF NOT EXISTS idx_photo_index_capture_utc
                    ON photo_index(capture_utc DESC);

                    CREATE INDEX IF NOT EXISTS idx_photo_index_has_gps_capture_utc
                    ON photo_index(has_gps, capture_utc DESC);
                    """;
                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize sync database at {DbPath}.", DbPath);
            throw;
        }
    }

    private static SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={DbPath};Mode=ReadWriteCreate;Pooling=True");
    }

    private static DateTimeOffset? ReadDateTimeOffset(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            return null;
        }

        return DateTimeOffset.TryParse(reader.GetString(index), out var value) ? value : null;
    }
}
