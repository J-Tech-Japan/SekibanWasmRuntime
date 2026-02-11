#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

WASM_PROJ="$ROOT/src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj"
OUT_DIR="$ROOT/artifacts/wasm"
PUBLISH_DIR="$OUT_DIR/publish"

mkdir -p "$OUT_DIR"

echo "[build-wasm] Publishing WASM module..."
dotnet publish "$WASM_PROJ" -c Release -r wasi-wasm -o "$PUBLISH_DIR"

WASM_FILE=$(find "$PUBLISH_DIR" -maxdepth 1 -name '*.wasm' -type f | head -n 1)
if [[ -z "$WASM_FILE" ]]; then
  echo "[build-wasm] ERROR: No .wasm file found in $PUBLISH_DIR" >&2
  exit 1
fi

cp "$WASM_FILE" "$OUT_DIR/sekibanwasm.wasm"
echo "[build-wasm] WASM module built: $OUT_DIR/sekibanwasm.wasm"
