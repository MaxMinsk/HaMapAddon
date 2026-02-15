#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <github-owner> <repo-name>" >&2
  echo "Example: $0 maximkaz people-map-plus-ha-addon" >&2
  exit 1
fi

OWNER="$1"
REPO="$2"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

sed -i.bak \
  "s|https://github.com/CHANGE_ME/people-map-plus-ha-addon|https://github.com/${OWNER}/${REPO}|g" \
  "${ROOT_DIR}/repository.yaml" \
  "${ROOT_DIR}/addons/people_map_plus/config.yaml"

sed -i.bak \
  "s|ghcr.io/CHANGE_ME/people-map-plus-addon-|ghcr.io/${OWNER}/people-map-plus-addon-|g" \
  "${ROOT_DIR}/addons/people_map_plus/config.yaml"

rm -f "${ROOT_DIR}/repository.yaml.bak" "${ROOT_DIR}/addons/people_map_plus/config.yaml.bak"

echo "Updated owner/repo references to ${OWNER}/${REPO}."

