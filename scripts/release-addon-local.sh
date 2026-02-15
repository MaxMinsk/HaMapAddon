#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <version>" >&2
  echo "Example: $0 0.1.0" >&2
  exit 1
fi

VERSION="$1"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG_FILE="${ROOT_DIR}/addons/people_map_plus/config.yaml"

if [[ ! -f "${CONFIG_FILE}" ]]; then
  echo "Missing ${CONFIG_FILE}" >&2
  exit 1
fi

if [[ ! "${VERSION}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must follow semver, e.g. 0.1.0" >&2
  exit 1
fi

sed -i.bak "s/^version:[[:space:]]*\"[0-9]\+\.[0-9]\+\.[0-9]\+\"/version: \"${VERSION}\"/" "${CONFIG_FILE}"
rm -f "${CONFIG_FILE}.bak"

bash "${ROOT_DIR}/scripts/validate-addon.sh"
bash "${ROOT_DIR}/scripts/release-check.sh" "${VERSION}"

git add "${CONFIG_FILE}" "${ROOT_DIR}/scripts/validate-addon.sh" "${ROOT_DIR}/scripts/release-check.sh" \
  "${ROOT_DIR}/addons/people_map_plus" "${ROOT_DIR}/.github/workflows"

git commit -m "release(addon): v${VERSION}"
git tag "v${VERSION}"

echo "Created commit and tag v${VERSION}. Push with:"
echo "  git push origin main --tags"

