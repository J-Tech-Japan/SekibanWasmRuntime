#!/usr/bin/env bash
# Build the MoonBit projector wasm module + runtime manifest for the
# mooncakes external-consumer proof.
#
# Default: builds the committed sample (registry dependencies; requires the
# sekiban packages to be published on mooncakes.io).
# --local-packages: builds a STAGED COPY whose manifests are rewritten to
# path dependencies on src/lib/sekiban-moonbit — the committed manifests are
# never modified. Pre-publish validation only.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.Mooncakes.MbDecider"
ARTIFACT_DIR="artifacts/samples/mooncakes-mb-decider"
STAGED_DIR="$ROOT/$ARTIFACT_DIR/staged"
MODULES_DIR="$ROOT/$ARTIFACT_DIR/modules"
CONFIG_DIR="$ROOT/$ARTIFACT_DIR/config"
MODULE_NAME="mooncakes-mb-decider.wasm"

MODE="registry"
if [[ "${1:-}" == "--local-packages" ]]; then
  MODE="local-packages"
  shift
fi

mkdir -p "$MODULES_DIR" "$CONFIG_DIR"

if [[ "$MODE" == "local-packages" ]]; then
  echo "[build-wasm] staging sample copy with path dependencies on src/lib/sekiban-moonbit (pre-publish dry-run)"
  rm -rf "$STAGED_DIR"
  mkdir -p "$STAGED_DIR"
  cp -R "$SAMPLE_DIR/wasm" "$STAGED_DIR/wasm"
  cp -R "$SAMPLE_DIR/client" "$STAGED_DIR/client"
  python3 - "$STAGED_DIR" "$ROOT" <<'PY'
import json, pathlib, sys
staged, root = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
mapping = {
    "sekiban/sekiban-wasm-runtime": str(root / "src/lib/sekiban-moonbit/wasm-runtime"),
    "sekiban/sekiban-client": str(root / "src/lib/sekiban-moonbit/client"),
}
for mod in ["wasm", "client"]:
    p = staged / mod / "moon.mod.json"
    data = json.loads(p.read_text())
    for dep, path in mapping.items():
        if dep in data.get("deps", {}):
            data["deps"][dep] = {"path": path}
    p.write_text(json.dumps(data, indent=2) + "\n")
    print(f"[build-wasm] rewrote {p} to path dependencies")
PY
  WASM_MODULE_DIR="$STAGED_DIR/wasm"
else
  WASM_MODULE_DIR="$ROOT/$SAMPLE_DIR/wasm"
fi

echo "[build-wasm] building MoonBit WASM module (mode=$MODE)"
(
  cd "$WASM_MODULE_DIR"
  moon build --target wasm --release
)

WASM_FILE="$(find "$WASM_MODULE_DIR/_build" -name "*.wasm" -path "*release*" | head -1)"
if [[ -z "$WASM_FILE" || ! -s "$WASM_FILE" ]]; then
  WASM_FILE="$(find "$WASM_MODULE_DIR" -name "*.wasm" -not -path "*/.mooncakes/*" | head -1)"
fi
if [[ -z "$WASM_FILE" || ! -s "$WASM_FILE" ]]; then
  echo "[build-wasm] ERROR: no MoonBit WASM module was produced." >&2
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
