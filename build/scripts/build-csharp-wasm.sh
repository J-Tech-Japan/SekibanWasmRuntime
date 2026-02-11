#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

WASM_PROJ="$ROOT/src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj"
PUBLISH_DIR="$ROOT/artifacts/csharp-wasm"
MODULES_DIR="$ROOT/src/internalUsage/modules"

mkdir -p "$PUBLISH_DIR" "$MODULES_DIR"

echo "[build-csharp-wasm] Publishing C# WASM module..."
dotnet publish "$WASM_PROJ" -c Release -r wasi-wasm -o "$PUBLISH_DIR"

WASM_FILE=$(find "$PUBLISH_DIR" -maxdepth 1 -name '*.wasm' -type f | head -n 1)
if [[ -z "$WASM_FILE" ]]; then
  echo "[build-csharp-wasm] ERROR: No .wasm file found in $PUBLISH_DIR" >&2
  exit 1
fi

cp "$WASM_FILE" "$MODULES_DIR/csharp-weather.wasm"
echo "[build-csharp-wasm] built: $MODULES_DIR/csharp-weather.wasm"
