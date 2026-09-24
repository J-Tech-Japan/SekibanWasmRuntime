#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="."

scan_manifests() {
  local pattern="$1"
  if command -v rg >/dev/null 2>&1; then
    rg -ni --glob 'Cargo.toml' --glob '!vendor/**' "$pattern" "$SAMPLE_DIR"
  else
    find "$SAMPLE_DIR" -type f -name Cargo.toml ! -path '*/vendor/*' -exec grep -Eni "$pattern" {} +
  fi
}

fail_on_manifest_match() {
  local pattern="$1" message="$2" matches="" status=0
  matches="$(scan_manifests "$pattern")" || status=$?
  if [[ "$status" -gt 1 ]]; then
    echo "could not scan Cargo manifests" >&2
    exit 1
  fi
  if [[ "$status" -eq 0 && -n "$matches" ]]; then
    echo "$message" >&2
    printf '%s\n' "$matches" >&2
    exit 1
  fi
}

fail_on_manifest_match \
  'sekiban-wasm-domain' \
  'forbidden sekiban-wasm-domain dependency found'

path_matches=""
path_status=0
path_matches="$(scan_manifests 'path[[:space:]]*=')" || path_status=$?
if [[ "$path_status" -gt 1 ]]; then
  echo "could not scan Cargo manifests for path dependencies" >&2
  exit 1
fi
if [[ "$path_status" -eq 0 && -n "$path_matches" ]]; then
  forbidden_paths=""
  while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    if ! printf '%s' "$line" | grep -Eiq 'wasm-projectors|sekiban-(core|derive|mv|wasm|executor|domain)'; then
      continue
    fi
    if printf '%s' "$line" | grep -Eq 'path[[:space:]]*=[[:space:]]*"(vendor/sekiban-mv|\.\./vendor/sekiban-mv)"'; then
      continue
    fi
    forbidden_paths+="${line}"$'\n'
  done <<< "$path_matches"
  if [[ -n "$forbidden_paths" ]]; then
    echo "forbidden local Sekiban path dependency found" >&2
    printf '%s' "$forbidden_paths" >&2
    exit 1
  fi
fi

contains_pattern() {
  local pattern="$1" file="$2"
  if command -v rg >/dev/null 2>&1; then
    rg -q "$pattern" "$file"
  else
    grep -Eq "$pattern" "$file"
  fi
}

# The end-to-end smoke must target the public GHCR runtime image, not a locally
# built runtime, so the sample proves published artifacts only. Assert the
# AppHost still references the public image.
APPHOST_PROGRAM="$SAMPLE_DIR/AppHost/Program.cs"
if [[ ! -f "$APPHOST_PROGRAM" ]]; then
  echo "missing AppHost Program.cs for the public GHCR runtime orchestration" >&2
  exit 1
fi
if ! contains_pattern 'ghcr\.io/j-tech-japan/sekiban-wasm-runtime-host' "$APPHOST_PROGRAM"; then
  echo "AppHost must target the public GHCR runtime image ghcr.io/j-tech-japan/sekiban-wasm-runtime-host" >&2
  exit 1
fi

SEKIBAN_MV_PIN_VERSION="0.1.1"
mv_publish_code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 \
  -H 'User-Agent: sekiban-wasm-runtime-registry-guard (+https://github.com/J-Tech-Japan/SekibanWasmRuntime)' \
  "https://crates.io/api/v1/crates/sekiban-mv/${SEKIBAN_MV_PIN_VERSION}" || echo "000")"
if [[ "$mv_publish_code" == "200" ]]; then
  cargo metadata --manifest-path "$SAMPLE_DIR/Cargo.toml" --format-version 1 >/dev/null
  cargo check --manifest-path "$SAMPLE_DIR/Cargo.toml" --workspace
else
  echo "[verify-no-local-sekiban-paths] sekiban-mv ${SEKIBAN_MV_PIN_VERSION} is not on crates.io yet; static guard passed (pre-publish live cargo check deferred)"
fi

echo "crates.io Rust sample dependency guard passed"
