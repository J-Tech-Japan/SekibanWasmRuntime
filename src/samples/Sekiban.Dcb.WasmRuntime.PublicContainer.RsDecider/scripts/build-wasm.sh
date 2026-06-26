#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider"
MANIFEST="$SAMPLE_DIR/Cargo.toml"
ARTIFACT_DIR="artifacts/samples/public-container-rs-decider"
MODULES_DIR="$ROOT/$ARTIFACT_DIR/modules"
CONFIG_DIR="$ROOT/$ARTIFACT_DIR/config"
MODULE_NAME="public-container-rs-decider.wasm"

mkdir -p "$MODULES_DIR" "$CONFIG_DIR"

echo "[build-wasm] building Rust WASM package public-container-rs-decider-wasm"
cargo build --manifest-path "$MANIFEST" --package public-container-rs-decider-wasm --target wasm32-wasip1 --release

WASM_FILE="$ROOT/$SAMPLE_DIR/target/wasm32-wasip1/release/public_container_rs_decider_wasm.wasm"
if [[ ! -s "$WASM_FILE" ]]; then
  WASM_FILE="$ROOT/target/wasm32-wasip1/release/sekiban_wasm_projector.wasm"
fi
if [[ ! -s "$WASM_FILE" ]]; then
  echo "[build-wasm] ERROR: no Rust WASM module was produced." >&2
  exit 1
fi

cp "$WASM_FILE" "$MODULES_DIR/$MODULE_NAME"
echo "[build-wasm] module: $ARTIFACT_DIR/modules/$MODULE_NAME ($(wc -c < "$MODULES_DIR/$MODULE_NAME") bytes)"

cat > "$CONFIG_DIR/sekiban-manifest.json" <<JSON
{
  "defaultModulePath": "/app/modules/$MODULE_NAME",
  "queryAssemblyVersion": "wasm",
  "eventTypes": [
    "WeatherForecastCreated",
    "WeatherForecastLocationUpdated",
    "WeatherForecastDeleted"
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
    "GetWeatherForecastListQuery": "WeatherForecastMultiProjection",
    "WeatherForecastListQuery": "WeatherForecastMultiProjection"
  },
  "materializedViews": [
    {
      "viewName": "WeatherForecast",
      "viewVersion": 1,
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
