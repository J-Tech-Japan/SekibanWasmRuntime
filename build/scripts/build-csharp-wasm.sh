#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

WASM_PROJ="$ROOT/src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj"
PUBLISH_DIR="$ROOT/artifacts/csharp-wasm"
MODULES_DIR="$ROOT/src/internalUsage/modules"
EXPECTED_WASM_NAME="SekibanWasm.Wasm.wasm"

mkdir -p "$PUBLISH_DIR" "$MODULES_DIR"

echo "[build-csharp-wasm] Publishing C# WASM module..."
echo "[build-csharp-wasm]   project:    $WASM_PROJ"
echo "[build-csharp-wasm]   publish-dir: $PUBLISH_DIR"

if ! dotnet publish "$WASM_PROJ" -c Release -r wasi-wasm -o "$PUBLISH_DIR"; then
  echo "[build-csharp-wasm] ERROR: dotnet publish failed." >&2
  echo "[build-csharp-wasm] Check the following in SekibanWasm.Wasm.csproj:" >&2
  echo "[build-csharp-wasm]   - PublishTrimmed should be false" >&2
  echo "[build-csharp-wasm]   - IlcTrimMetadata should be false" >&2
  echo "[build-csharp-wasm]   - RuntimeIdentifier should be wasi-wasm" >&2
  exit 1
fi

echo "[build-csharp-wasm] Publish succeeded. Scanning for .wasm output..."

WASM_FILE="$PUBLISH_DIR/$EXPECTED_WASM_NAME"
if [[ ! -f "$WASM_FILE" ]]; then
  echo "[build-csharp-wasm] Expected $EXPECTED_WASM_NAME not found; searching for any .wasm file..." >&2
  WASM_FILE=$(find "$PUBLISH_DIR" -maxdepth 1 -name '*.wasm' -type f | head -n 1)
fi

if [[ -z "$WASM_FILE" || ! -f "$WASM_FILE" ]]; then
  echo "[build-csharp-wasm] ERROR: No .wasm file found in $PUBLISH_DIR" >&2
  echo "[build-csharp-wasm] Contents of publish directory:" >&2
  ls -la "$PUBLISH_DIR" >&2
  exit 1
fi

cp "$WASM_FILE" "$MODULES_DIR/csharp-weather.wasm"
echo "[build-csharp-wasm] built: $MODULES_DIR/csharp-weather.wasm ($(wc -c < "$MODULES_DIR/csharp-weather.wasm") bytes)"
