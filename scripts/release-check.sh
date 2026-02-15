#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <version>" >&2
  exit 1
fi

VERSION="$1"
CONFIG_FILE="addons/people_map_plus/config.yaml"

if [[ ! -f "${CONFIG_FILE}" ]]; then
  echo "Missing ${CONFIG_FILE}" >&2
  exit 1
fi

CONFIG_VERSION="$(awk -F'"' '/^version:[[:space:]]*"/ { print $2; exit }' "${CONFIG_FILE}")"

if [[ -z "${CONFIG_VERSION}" ]]; then
  echo "Cannot parse version from ${CONFIG_FILE}" >&2
  exit 1
fi

if [[ "${CONFIG_VERSION}" != "${VERSION}" ]]; then
  echo "Tag version (${VERSION}) must match config version (${CONFIG_VERSION})" >&2
  exit 1
fi

echo "Release check passed for version ${VERSION}."
