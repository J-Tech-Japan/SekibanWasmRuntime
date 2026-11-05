#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="."
MANIFEST="$SAMPLE_DIR/Cargo.toml"
ARTIFACT_DIR="artifacts/samples/__SEKIBAN_PROJECT_KEBAB__"
MODULES_DIR="$ROOT/$ARTIFACT_DIR/modules"
CONFIG_DIR="$ROOT/$ARTIFACT_DIR/config"
MODULE_NAME="__SEKIBAN_PROJECT_KEBAB__.wasm"

mkdir -p "$MODULES_DIR" "$CONFIG_DIR"

echo "[build-wasm] building Rust WASM package __SEKIBAN_PROJECT_KEBAB__-wasm from crates.io Sekiban crates"
cargo build --manifest-path "$MANIFEST" --package __SEKIBAN_PROJECT_KEBAB__-wasm --target wasm32-wasip1 --release

WASM_FILE="$ROOT/$SAMPLE_DIR/target/wasm32-wasip1/release/__SEKIBAN_PROJECT_SNAKE___wasm.wasm"
if [[ ! -s "$WASM_FILE" ]]; then
  echo "[build-wasm] ERROR: no Rust WASM module was produced." >&2
  exit 1
fi

cp "$WASM_FILE" "$MODULES_DIR/$MODULE_NAME"
echo "[build-wasm] module: $ARTIFACT_DIR/modules/$MODULE_NAME ($(wc -c < "$MODULES_DIR/$MODULE_NAME") bytes)"

MODULE_SHA256="$(sha256sum "$MODULES_DIR/$MODULE_NAME" | awk '{print $1}')"

cat > "$CONFIG_DIR/sekiban-manifest.json" <<JSON
{
  "defaultModulePath": "/app/modules/$MODULE_NAME",
  "queryAssemblyVersion": "wasm",
  "eventTypes": [
    "WeatherForecastCreated",
    "WeatherForecastLocationUpdated"
  ],
  "projectors": [
    {
      "projectorName": "WeatherForecastProjector",
      "modulePath": "/app/modules/$MODULE_NAME",
      "abiKind": "wasi-preview1",
      "moduleVersion": "1.0.0",
      "projectorVersion": "1.0.0"
    },
    {
      "projectorName": "WeatherForecastMultiProjection",
      "modulePath": "/app/modules/$MODULE_NAME",
      "abiKind": "wasi-preview1",
      "moduleVersion": "1.0.0",
      "projectorVersion": "1.0.0"
    }
  ],
  "queryProjectors": {
    "GetWeatherForecastCountQuery": "WeatherForecastMultiProjection",
    "GetWeatherForecastListQuery": "WeatherForecastMultiProjection"
  },
  "materializedViews": [
    {
      "viewName": "WeatherForecast",
      "viewVersion": 1,
      "abiVersion": "sekiban-wasm-mv/1",
      "capabilities": ["query-rows"],
      "moduleSha256": "$MODULE_SHA256",
      "modulePath": "/app/modules/$MODULE_NAME",
      "logicalTables": [
        "weather_forecast"
      ]
    }
  ]
}
JSON

echo "[build-wasm] manifest: $ARTIFACT_DIR/config/sekiban-manifest.json"
echo "[build-wasm] OK"
