#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider"
ARTIFACT_DIR="artifacts/samples/public-spm-swift-decider"
MODULES_DIR="$ROOT/$ARTIFACT_DIR/modules"
CONFIG_DIR="$ROOT/$ARTIFACT_DIR/config"
MODULE_NAME="public-spm-swift-decider.wasm"
SDK="${SWIFT_WASM_SDK:-swift-6.3.1-RELEASE_wasm}"

if [[ -f "$HOME/.swiftly/env.sh" ]]; then
  # shellcheck disable=SC1091
  . "$HOME/.swiftly/env.sh"
fi
hash -r

mkdir -p "$MODULES_DIR" "$CONFIG_DIR"

echo "[build-wasm] building Swift WASM module (swift-sdk=$SDK); dependency resolution follows the current SwiftPM mirror configuration"
(
  cd "$SAMPLE_DIR"
  swift build --swift-sdk "$SDK" -c release
)

WASM_FILE="$ROOT/$SAMPLE_DIR/.build/release/PublicSpmSwiftDecider.wasm"
if [[ ! -s "$WASM_FILE" ]]; then
  echo "[build-wasm] ERROR: no Swift WASM module was produced." >&2
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
