# OneDrive Setup For Add-on

## 1. Create Azure app registration

1. Open Azure Portal -> Entra ID -> App registrations -> New registration.
2. Name it (for example `PeopleMapPlusSync`).
3. Supported account types:
   - personal only: `Personal Microsoft accounts only`
   - mixed org/personal: `Accounts in any organizational directory and personal Microsoft accounts`
4. Redirect URI is not required for device code flow.

## 2. API permissions

Add Microsoft Graph delegated permissions:

1. `Files.Read`
2. `User.Read`
3. `offline_access`

Grant consent where required.

## 3. Client secret (optional but recommended for confidential app)

1. App registrations -> your app -> Certificates & secrets -> New client secret.
2. Save generated secret value.

## 4. Fill Home Assistant add-on Configuration

1. `onedrive_enabled: true`
2. `onedrive_client_id: <app client id>`
3. `onedrive_client_secret: <secret>` (if used)
4. `onedrive_refresh_token: ""` (leave empty for device code flow)
5. `onedrive_tenant: consumers` (or your tenant id)
6. `onedrive_folder_path: /` (or specific folder)
7. `lookback_days: 5`
8. `sync_interval_hours: 24`

Then Save and restart the add-on.

## 5. Connect from add-on page (Device Code)

1. Open add-on panel (ingress) and click `Connect to OneDrive`.
2. Copy `user_code` and open shown Microsoft verification URL.
3. Approve access in your Microsoft account.
4. Back in add-on panel click `Check Authorization`.
5. Add-on stores refresh token in `/data/onedrive_tokens.json`.

After this, scheduled sync works without manually pasting tokens.
