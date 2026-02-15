# OneDrive Setup For Add-on

## 1. Create Azure app registration

1. Open Azure Portal -> Entra ID -> App registrations -> New registration.
2. Name it (for example `PeopleMapPlusSync`).
3. Supported account types:
   - personal only: `Personal Microsoft accounts only`
   - mixed org/personal: `Accounts in any organizational directory and personal Microsoft accounts`
4. Redirect URI (mobile/desktop): `http://localhost`.

## 2. API permissions

Add Microsoft Graph delegated permissions:

1. `Files.Read`
2. `User.Read`
3. `offline_access`

Grant consent where required.

## 3. Client secret (optional but recommended for confidential app)

1. App registrations -> your app -> Certificates & secrets -> New client secret.
2. Save generated secret value.

## 4. Get refresh token

Use OAuth authorization code flow once, then exchange code for refresh token.

Token endpoint pattern:

`https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token`

For personal account tenant usually `consumers`.

## 5. Fill Home Assistant add-on Configuration

1. `onedrive_enabled: true`
2. `onedrive_client_id: <app client id>`
3. `onedrive_client_secret: <secret>` (if used)
4. `onedrive_refresh_token: <refresh token>`
5. `onedrive_tenant: consumers` (or your tenant id)
6. `onedrive_folder_path: /` (or specific folder)
7. `lookback_days: 5`
8. `sync_interval_hours: 24`

Then Save and restart the add-on.

