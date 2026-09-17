#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider"

scan_cargo() {
  local pattern="$1"
  if command -v rg >/dev/null 2>&1; then
    rg -ni --glob 'Cargo.toml' "$pattern" "$SAMPLE_DIR"
  else
    find "$SAMPLE_DIR" -type f -name Cargo.toml -exec grep -Eni "$pattern" {} +
  fi
}

search_quiet() {
  local pattern="$1" file="$2"
  if command -v rg >/dev/null 2>&1; then
    rg -q "$pattern" "$file"
  else
    grep -Eq "$pattern" "$file"
  fi
}

matches=""
status=0
matches="$(scan_cargo 'path[[:space:]]*=.*wasm-projectors/rust|sekiban-wasm-domain')" || status=$?
if [[ "$status" -gt 1 ]]; then
  echo "could not scan Cargo manifests" >&2
  exit 1
fi
if [[ "$status" -eq 0 && -n "$matches" ]]; then
  echo "forbidden local Sekiban path dependency or sekiban-wasm-domain dependency found" >&2
  printf '%s\n' "$matches" >&2
  exit 1
fi

# The end-to-end smoke must target the public GHCR runtime image, not a locally
# built runtime, so the sample proves published artifacts only. Assert the
# AppHost still references the public image.
APPHOST_PROGRAM="$SAMPLE_DIR/AppHost/Program.cs"
if [[ ! -f "$APPHOST_PROGRAM" ]]; then
  echo "missing AppHost Program.cs for the public GHCR runtime orchestration" >&2
  exit 1
fi
if ! search_quiet 'ghcr\.io/j-tech-japan/sekiban-wasm-runtime-host' "$APPHOST_PROGRAM"; then
  echo "AppHost must target the public GHCR runtime image ghcr.io/j-tech-japan/sekiban-wasm-runtime-host" >&2
  exit 1
fi

cargo metadata --manifest-path "$SAMPLE_DIR/Cargo.toml" --format-version 1 >/dev/null
cargo check --manifest-path "$SAMPLE_DIR/Cargo.toml" --workspace

echo "crates.io Rust sample dependency guard passed"
