#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

RUST_DIR="$ROOT/src/wasm-projectors/rust"
MODULES_DIR="$ROOT/src/internalUsage/modules"

if [[ ! -d "$RUST_DIR" ]]; then
  echo "[build-rust-wasm] SKIP: Rust workspace not found at $RUST_DIR" >&2
  exit 0
fi

mkdir -p "$MODULES_DIR"

echo "[build-rust-wasm] Building Rust WASM module..."
cargo build --manifest-path "$RUST_DIR/Cargo.toml" --target wasm32-wasip1 --release

RUST_TARGET_DIR="$RUST_DIR/target"
WASM_FILE="$RUST_TARGET_DIR/wasm32-wasip1/release/weather_projector.wasm"
if [[ ! -f "$WASM_FILE" ]]; then
  WASM_FILE=$(find "$RUST_TARGET_DIR/wasm32-wasip1/release" -maxdepth 1 -name '*.wasm' -type f | head -n 1)
fi

if [[ -z "$WASM_FILE" || ! -f "$WASM_FILE" ]]; then
  echo "[build-rust-wasm] ERROR: No .wasm file found in target/wasm32-wasip1/release" >&2
  exit 1
fi

cp "$WASM_FILE" "$MODULES_DIR/rust-weather.wasm"
echo "[build-rust-wasm] built: $MODULES_DIR/rust-weather.wasm"
