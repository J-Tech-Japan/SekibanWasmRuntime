#!/usr/bin/env bash
# Validate that both public npm packages declare the expected synchronized
# version. Used by the ts-v* npm release lane before any publish so a GitHub
# Release tag can only publish when the package.json files already match, and
# runnable locally the same way.
#
# Usage: scripts/release/check-npm-package-versions.sh <expected-version>
#        (accepts `0.1.0`, `v0.1.0`, or the full tag `ts-v0.1.0`)
set -euo pipefail

EXPECTED="${1:?usage: check-npm-package-versions.sh <expected-version>}"
EXPECTED="${EXPECTED#ts-v}"
EXPECTED="${EXPECTED#v}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [[ ! "$EXPECTED" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$ ]]; then
  echo "ERROR: expected version '${EXPECTED}' is not a semver version." >&2
  exit 1
fi

PACKAGES=(
  "src/lib/sekiban-ts"
  "src/lib/sekiban-as-wasm"
)
mismatch=0

for package_dir in "${PACKAGES[@]}"; do
  manifest="${ROOT}/${package_dir}/package.json"
  name="$(node -p "require('${manifest}').name")"
  version="$(node -p "require('${manifest}').version")"
  if [ "${version}" != "${EXPECTED}" ]; then
    echo "ERROR: ${name} (${package_dir}) version ${version} does not match expected ${EXPECTED}" >&2
    mismatch=1
  else
    echo "ok: ${name} ${version}"
  fi
done

if [ "${mismatch}" -ne 0 ]; then
  echo "ERROR: public npm package versions are not synchronized to ${EXPECTED}." >&2
  exit 1
fi

echo "All ${#PACKAGES[@]} public npm packages are at ${EXPECTED}."
