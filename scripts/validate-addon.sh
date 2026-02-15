#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ADDON_DIR="${ROOT_DIR}/addons/people_map_plus"

required_files=(
  "${ROOT_DIR}/repository.yaml"
  "${ADDON_DIR}/config.yaml"
  "${ADDON_DIR}/Dockerfile"
  "${ADDON_DIR}/src/PeopleMapPlus.Addon.csproj"
  "${ADDON_DIR}/src/Program.cs"
)

for file in "${required_files[@]}"; do
  if [[ ! -f "${file}" ]]; then
    echo "Missing required file: ${file}" >&2
    exit 1
  fi
done

if ! grep -Eq '^slug:\s*"people_map_plus"$|^slug:\s*people_map_plus$' "${ADDON_DIR}/config.yaml"; then
  echo "config.yaml must contain slug: people_map_plus" >&2
  exit 1
fi

if ! grep -Eq '^version:\s*"[0-9]+\.[0-9]+\.[0-9]+"' "${ADDON_DIR}/config.yaml"; then
  echo "config.yaml version must follow semver (X.Y.Z)" >&2
  exit 1
fi

if ! grep -Eq 'image:\s*"ghcr.io/.+/people-map-plus-addon-\{arch\}"' "${ADDON_DIR}/config.yaml"; then
  echo "config.yaml image must use ghcr.io/<owner>/people-map-plus-addon-{arch}" >&2
  exit 1
fi

if ! grep -Eq '^ingress:\s*true$' "${ADDON_DIR}/config.yaml"; then
  echo "config.yaml must enable ingress for addon API access from HA UI" >&2
  exit 1
fi

echo "Addon scaffold validation passed."
