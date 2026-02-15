# People Map Plus HA Add-on Repository

Infrastructure scaffold for developing and releasing a Home Assistant add-on with GitHub Actions and GHCR.

## What is included

1. Home Assistant add-on repository structure (`repository.yaml`, `addons/people_map_plus/*`) with C# backend scaffold.
2. VS Code workspace setup (`.vscode/*`) and reusable tasks.
3. CI workflow for validation and test build (`.github/workflows/ci.yml`).
4. Release workflow for amd64 image publishing to GHCR (`.github/workflows/release-addon.yml`).
5. Helper scripts for validation and release checks (`scripts/*`).

## First-time setup

1. Replace `CHANGE_ME` in `repository.yaml` and `addons/people_map_plus/config.yaml` with your GitHub org/user and contacts.
2. Or run helper:
   `bash scripts/set-github-owner.sh <github-owner> <repo-name>`
3. Ensure GitHub repository is public.
4. In repository settings, keep `GITHUB_TOKEN` package permissions enabled (default for Actions with `packages: write` in workflow).
5. Commit and push to `main`.

## Release flow (to make `Update` appear in HA)

1. Bump `version` in `addons/people_map_plus/config.yaml` (for example `0.1.1`).
2. Commit and push to `main`.
3. Create and push tag `v0.1.1`.
4. Workflow publishes images to:
   `ghcr.io/<owner>/people-map-plus-addon-amd64:0.1.1`
5. Home Assistant checks the add-on repo and shows `Update` when new `version` is detected.

Quick local helper:

1. `bash scripts/release-addon-local.sh 0.1.1`
2. `git push origin main --tags`

## Install in Home Assistant

1. Open **Settings -> Add-ons -> Add-on Store**.
2. Add repository URL from `repository.yaml` (`https://github.com/<owner>/<repo>`).
3. Find **People Map Plus Backend** in store and install.

## Local VS Code tasks

1. `Addon: Validate repository` runs scaffold checks.
2. `Addon: Build local image (amd64)` builds test container image.

## Notes

1. Current add-on runtime is ASP.NET Core (`net8.0`) with OneDrive sync and Device Code connect flow from ingress UI.
2. UI card distribution options are documented in `docs/ui-card-distribution-options.md`.
3. HACS-ready UI card repository template is available in `card-repo-template/`.
4. OneDrive setup notes: `docs/onedrive-setup.md`.
