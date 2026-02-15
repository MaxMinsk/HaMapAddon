# People Map Plus Backend Add-on

This Home Assistant add-on (C# / ASP.NET Core) hosts backend services for:

1. Photo indexing from `/media` using EXIF GPS metadata.
2. API endpoints used by the `custom:people-map-plus` Lovelace card.

Current state:

1. Running C# backend container.
2. Health endpoints:
   - `/health`
   - `/api/people_map_plus/health`
3. API business logic is still scaffold-level and will be implemented next.
