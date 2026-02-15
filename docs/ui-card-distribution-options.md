# UI Card Distribution Options

## Option 1: HACS frontend plugin (recommended)

1. Publish card source in a separate public repo.
2. Build minified bundle into `dist/people-map-plus.js`.
3. Add `hacs.json` and release tags.
4. Users install/update card through HACS UI.

Pros:

1. Best user experience for installs and updates.
2. Common pattern in Home Assistant community.

Cons:

1. Requires HACS installation on user side.

## Option 2: Card assets shipped from add-on API

1. Add-on serves bundled JS for card from backend endpoint.
2. Lovelace resource points to local HA URL.

Pros:

1. Single backend repo/release flow.

Cons:

1. Tighter coupling between backend and frontend versions.
2. More custom plumbing and caching concerns.

## Option 3: Manual resource from GitHub Releases/CDN

1. Publish `people-map-plus.js` as release asset.
2. Users add resource URL manually in dashboard settings.

Pros:

1. Simple for initial bootstrap.

Cons:

1. Weak update UX.
2. More user error during setup.

## Recommendation

Start with Option 1 (HACS) for UI card, keep add-on backend in current repository.  
This separates release cadence and makes updates predictable for both backend and frontend.

