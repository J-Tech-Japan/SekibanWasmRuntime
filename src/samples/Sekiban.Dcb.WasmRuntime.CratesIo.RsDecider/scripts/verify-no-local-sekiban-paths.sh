#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider"

if rg -n 'path\s*=.*wasm-projectors/rust|sekiban-wasm-domain' "$SAMPLE_DIR" -g Cargo.toml; then
  echo "forbidden local Sekiban path dependency or sekiban-wasm-domain dependency found" >&2
  exit 1
fi

cargo metadata --manifest-path "$SAMPLE_DIR/Cargo.toml" --format-version 1 >/dev/null
cargo check --manifest-path "$SAMPLE_DIR/Cargo.toml" --workspace

echo "crates.io Rust sample dependency guard passed"

