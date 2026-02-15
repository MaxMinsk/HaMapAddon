using Microsoft.Data.Sqlite;

namespace PeopleMapPlus.Addon;

public sealed record DownloadedFileRecord(
    string ItemId,
    string? ETag,
    long? SizeBytes,
    DateTimeOffset? LastModifiedUtc,
    string LocalPath
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
}
