# People Map Plus Backend Add-on

This Home Assistant add-on (C# / ASP.NET Core) hosts backend services for:

1. Photo indexing from `/media` using EXIF GPS metadata.
2. API endpoints used by the `custom:people-map-plus` Lovelace card.
3. OneDrive sync (daily or custom interval) with download to `/media`.

## Runtime

1. Running C# backend container.
2. SQLite state in `/data/people_map_plus_sync.db`.
3. Key endpoints:
   - `/health`
   - `/api/people_map_plus/health`
   - `/api/people_map_plus/sync/status`
   - `POST /api/people_map_plus/sync/run`

## Configuration in Home Assistant UI

Configure from Add-on page -> **Configuration**:

1. `onedrive_enabled` - enable/disable sync.
2. `onedrive_client_id` - Azure app client id.
3. `onedrive_client_secret` - optional secret (required for confidential app).
4. `onedrive_refresh_token` - refresh token for delegated access.
5. `onedrive_tenant` - default `consumers` (for personal OneDrive).
6. `onedrive_scope` - default `offline_access Files.Read User.Read`.
7. `onedrive_drive_id` - optional custom drive id, empty means `me/drive`.
8. `onedrive_folder_path` - OneDrive folder path to sync, default `/`.
9. `destination_subdir` - destination under `/media`, default `people_map_plus/onedrive`.
10. `lookback_days` - how many days back to process (default 5).
11. `sync_interval_hours` - sync interval in hours (default 24).
12. `max_files_per_run` - safety cap per sync run.
13. `run_sync_on_startup` - run once when add-on starts.

## Sync behavior

1. Uses Microsoft Graph delta API and persists `deltaLink`.
2. Filters files by last modified date (`lookback_days`).
3. Downloads supported image types (`jpg`, `jpeg`, `png`, `heic`, `heif`, `webp`).
4. Avoids duplicates by OneDrive `item_id` + `eTag`.
