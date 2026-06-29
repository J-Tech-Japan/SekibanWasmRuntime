#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider"

if rg -n 'path\s*=.*wasm-projectors/rust|sekiban-wasm-domain' "$SAMPLE_DIR" -g Cargo.toml; then
  echo "forbidden local Sekiban path dependency or sekiban-wasm-domain dependency found" >&2
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
if ! rg -q 'ghcr\.io/j-tech-japan/sekiban-wasm-runtime-host' "$APPHOST_PROGRAM"; then
  echo "AppHost must target the public GHCR runtime image ghcr.io/j-tech-japan/sekiban-wasm-runtime-host" >&2
  exit 1
fi

cargo metadata --manifest-path "$SAMPLE_DIR/Cargo.toml" --format-version 1 >/dev/null
cargo check --manifest-path "$SAMPLE_DIR/Cargo.toml" --workspace

echo "crates.io Rust sample dependency guard passed"
