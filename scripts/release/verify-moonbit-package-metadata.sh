#!/usr/bin/env bash
# SWR-G064 MoonBit package metadata gate.
#
# Validates that both MoonBit packages under src/lib/sekiban-moonbit carry
# publish-ready mooncakes.io metadata and stay version-aligned:
#   * required fields present and non-empty (name, version, description,
#     repository, license, keywords, readme),
#   * expected package names and Elastic-2.0 license,
#   * README and LICENSE files exist in each package,
#   * both packages share the same version,
#   * optionally, the version matches a release tag passed as $1
#     (accepts `moonbit-vX.Y.Z` or plain `X.Y.Z`).
#
# Usage:
#   bash scripts/release/verify-moonbit-package-metadata.sh [moonbit-vX.Y.Z]
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"

TAG="${1:-}"

log() { printf '[verify-moonbit-package-metadata] %s\n' "$*"; }

errors=0
err() { log "ERROR: $*"; errors=$((errors + 1)); }

check_package() {
  local dir="$1" expected_name="$2"
  local mod="$dir/moon.mod.json"

  if [[ ! -f "$mod" ]]; then
    err "$mod is missing"
    return
  fi

  local name version description repository license keywords readme
  name="$(node -p "require('./$mod').name ?? ''")"
  version="$(node -p "require('./$mod').version ?? ''")"
  description="$(node -p "require('./$mod').description ?? ''")"
  repository="$(node -p "require('./$mod').repository ?? ''")"
  license="$(node -p "require('./$mod').license ?? ''")"
  keywords="$(node -p "(require('./$mod').keywords ?? []).length")"
  readme="$(node -p "require('./$mod').readme ?? ''")"

  [[ "$name" == "$expected_name" ]] || err "$mod: name is '$name', expected '$expected_name'"
  [[ -n "$version" ]] || err "$mod: version is missing"
  [[ -n "$description" ]] || err "$mod: description is missing"
  [[ "$repository" == "https://github.com/J-Tech-Japan/SekibanWasmRuntime" ]] \
    || err "$mod: repository is '$repository', expected the SekibanWasmRuntime GitHub URL"
  [[ "$license" == "Elastic-2.0" ]] || err "$mod: license is '$license', expected 'Elastic-2.0'"
  [[ "$keywords" -ge 3 ]] || err "$mod: keywords must list at least 3 entries (found $keywords)"
  [[ "$readme" == "README.md" ]] || err "$mod: readme is '$readme', expected 'README.md'"
  [[ -s "$dir/README.md" ]] || err "$dir/README.md is missing or empty"
  [[ -s "$dir/LICENSE" ]] || err "$dir/LICENSE is missing or empty"

  printf '%s' "$version"
}

wasm_version="$(check_package src/lib/sekiban-moonbit/wasm-runtime sekiban/sekiban-wasm-runtime)"
client_version="$(check_package src/lib/sekiban-moonbit/client sekiban/sekiban-client)"

if [[ -n "$wasm_version" && -n "$client_version" && "$wasm_version" != "$client_version" ]]; then
  err "version drift: sekiban-wasm-runtime=$wasm_version, sekiban-client=$client_version"
fi

if [[ -n "$TAG" ]]; then
  tag_version="${TAG#moonbit-v}"
  if [[ ! "$tag_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$ ]]; then
    err "tag '$TAG' does not match moonbit-vX.Y.Z"
  elif [[ "$tag_version" != "$wasm_version" ]]; then
    err "tag version $tag_version does not match package version $wasm_version"
  fi
fi

if [[ "$errors" -gt 0 ]]; then
  log "FAIL: $errors metadata problem(s)"
  exit 1
fi
log "OK: both packages carry publish-ready metadata at version $wasm_version${TAG:+ (tag $TAG consistent)}"
exit 0
