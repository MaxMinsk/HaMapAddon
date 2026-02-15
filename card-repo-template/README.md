# People Map Plus Card (HACS Template)

Template of a separate public repository for the Lovelace UI card distribution via HACS.

## Goal

Ship `custom:people-map-plus` as a HACS Dashboard plugin with predictable releases.

## Repository layout

```text
card-repo-template/
  hacs.json
  package.json
  tsconfig.json
  vite.config.ts
  src/
    people-map-plus-card.ts
    index.ts
  dist/                  # generated on build/release
  .github/workflows/
    ci.yml
    release.yml
```

## First setup

1. Create a new public GitHub repo, recommended name: `people-map-plus-card`.
2. Copy files from this template into that repo root.
3. Replace `CHANGE_ME` in `hacs.json`.
4. Run:
   - `npm ci`
   - `npm run build`
5. Create release tag `v0.1.0` (or newer); workflow creates GitHub Release with card bundle asset.

## HA install path

1. In HACS add custom repository:
   - URL: your card repo
   - Category: Dashboard
2. Install plugin in HACS.
3. Add card in dashboard with type:
   - `custom:people-map-plus`

