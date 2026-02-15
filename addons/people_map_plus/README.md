# People Map Plus Backend Add-on

This Home Assistant add-on (C# / ASP.NET Core) is focused on OneDrive photo sync:

1. OneDrive sync (daily or custom interval) with download to `/media`.
2. Automatic resize to configured `max_size`.
3. Thumbnail generation (`thumb_` files, max side `320px`).
4. Device Code authentication flow from add-on Web UI.

## Runtime

1. Running C# backend container.
2. SQLite state in `/data/people_map_plus_sync.db`.
3. Key endpoints:
   - `/health`
   - `/api/people_map_plus/health`
   - `/api/people_map_plus/sync/status`
   - `POST /api/people_map_plus/sync/run`
   - `/api/people_map_plus/onedrive/folders?path=/`
   - `POST /api/people_map_plus/onedrive/device/start`
   - `POST /api/people_map_plus/onedrive/device/poll`
   - `/api/people_map_plus/onedrive/device/status`

## Configuration in Home Assistant UI

Configure from Add-on page -> **Configuration**:

1. `onedrive_enabled` - enable/disable sync.
2. `onedrive_client_id` - Azure app client id.
3. `onedrive_client_secret` - optional secret (required for confidential app).
4. `onedrive_refresh_token` - optional manual refresh token (can be empty if using Connect button).
5. `onedrive_tenant` - default `consumers` (for personal OneDrive).
6. `onedrive_scope` - default `offline_access Files.Read User.Read`.
7. `onedrive_drive_id` - optional custom drive id, empty means `me/drive`.
8. `onedrive_folder_path` - OneDrive folder path to sync, default `/`.
9. `destination_subdir` - destination under `/media`, default `people_map_plus/onedrive`.
10. `lookback_days` - how many days back to process (default 5).
11. `sync_interval_hours` - sync interval in hours (default 24).
12. `max_files_per_run` - safety cap per sync run.
13. `max_size` - max side (px) after resize, default `2500`.
14. `run_sync_on_startup` - run once when add-on starts.

## Connect to OneDrive from add-on page

1. Save `onedrive_client_id` (and `onedrive_client_secret` if needed).
2. Open add-on panel (Info tab -> `Open Web UI`).
3. Click `Connect to OneDrive`.
4. Complete Microsoft verification with shown code.
5. Click `Check Authorization`.

Use `List Folders` in Web UI to discover valid `onedrive_folder_path` values without guessing.

## Sync behavior

1. Uses Microsoft Graph `children` traversal for stable folder scanning (with pagination).
2. Filters files by last modified date (`lookback_days`).
3. Downloads supported image types (`jpg`, `jpeg`, `png`, `heic`, `heif`, `webp`).
4. Avoids duplicates by OneDrive `item_id` + `eTag`.
5. Supports Device Code connect flow from ingress page (`Connect to OneDrive`).
6. Auto-resizes images larger than `max_size` on any side (aspect ratio preserved).
7. Writes log entry when resize is required and when resize is completed/skipped.
8. Generates `thumb_` preview copy for each image (max side `320px`) in the same folder.
