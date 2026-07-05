#!/usr/bin/env bash
# Builds the AssemblyScript WASM projector module + runtime manifest for the
# npm TypeScript external-consumer sample.
#
# SEKIBAN_NPM_MODE selects how @sekiban/as-wasm is resolved:
#   registry (default) - `npm install` against the npm registry. Requires
#                         @sekiban/as-wasm@0.1.0 to be published (SWR-G058).
#   tarball             - packs @sekiban/as-wasm from src/lib/sekiban-as-wasm
#                         and installs from that local tarball, so the build
#                         passes before publish.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider"
WASM_DIR="$ROOT/$SAMPLE_DIR/Wasm"
ARTIFACT_DIR="$ROOT/artifacts/samples/npm-ts-decider"
MODULES_DIR="$ARTIFACT_DIR/modules"
CONFIG_DIR="$ARTIFACT_DIR/config"
MODULE_NAME="npm-ts-decider.wasm"
NPM_MODE="${SEKIBAN_NPM_MODE:-registry}"

log() { printf '[build-wasm] %s\n' "$*"; }

mkdir -p "$MODULES_DIR" "$CONFIG_DIR"

BUILD_DIR="$WASM_DIR"
case "$NPM_MODE" in
  registry)
    log "SEKIBAN_NPM_MODE=registry: installing @sekiban/as-wasm@0.1.0 from the npm registry"
    if ! (cd "$WASM_DIR" && npm install --no-audit --no-fund); then
      echo "[build-wasm] npm registry install failed -- @sekiban/as-wasm is not published yet; re-run with SEKIBAN_NPM_MODE=tarball" >&2
      exit 1
    fi
    ;;
  tarball)
    log "SEKIBAN_NPM_MODE=tarball: packing @sekiban/as-wasm and installing from the local tarball"
    TARBALL_DIR="$ARTIFACT_DIR/tarballs"
    rm -rf "$TARBALL_DIR"
    mkdir -p "$TARBALL_DIR"
    AS_PKG_DIR="$ROOT/src/lib/sekiban-as-wasm"
    npm --prefix "$AS_PKG_DIR" install --no-audit --no-fund >/dev/null 2>&1 \
      || { echo "[build-wasm] npm install failed for @sekiban/as-wasm" >&2; exit 1; }
    AS_TGZ_NAME="$(cd "$AS_PKG_DIR" && npm pack --pack-destination "$TARBALL_DIR" --silent 2>/dev/null | tail -n1)"
    AS_TGZ="$TARBALL_DIR/$AS_TGZ_NAME"
    [[ -s "$AS_TGZ" ]] || { echo "[build-wasm] npm pack produced no tarball for @sekiban/as-wasm" >&2; exit 1; }

    BUILD_DIR="$ARTIFACT_DIR/wasm-build"
    rm -rf "$BUILD_DIR"
    mkdir -p "$BUILD_DIR"
    cp -R "$WASM_DIR/assembly" "$BUILD_DIR/assembly"
    cp "$WASM_DIR/asconfig.json" "$WASM_DIR/tsconfig.json" "$BUILD_DIR/"
    node -e "
      const fs = require('fs');
      const pkg = JSON.parse(fs.readFileSync('$WASM_DIR/package.json', 'utf8'));
      pkg.dependencies['@sekiban/as-wasm'] = 'file:$AS_TGZ';
      fs.writeFileSync('$BUILD_DIR/package.json', JSON.stringify(pkg, null, 2));
    "

    if ! (cd "$BUILD_DIR" && npm install --no-audit --no-fund); then
      echo "[build-wasm] npm install failed for the packed @sekiban/as-wasm tarball" >&2
      exit 1
    fi

    resolved="$(node -p "require('$BUILD_DIR/package-lock.json').packages['node_modules/@sekiban/as-wasm'].resolved || ''")"
    case "$resolved" in
      *sekiban-as-wasm-*.tgz) ;;
      *)
        echo "[build-wasm] no-local-path guard: @sekiban/as-wasm resolved to '$resolved' instead of the packed tarball" >&2
        exit 1
        ;;
    esac
    ;;
  *)
    echo "[build-wasm] unknown SEKIBAN_NPM_MODE=$NPM_MODE (expected 'registry' or 'tarball')" >&2
    exit 1
    ;;
esac

log "compiling AssemblyScript projector (SEKIBAN_NPM_MODE=$NPM_MODE)"
if ! (cd "$BUILD_DIR" && npx asc assembly/index.ts \
  --outFile "build/$MODULE_NAME" \
  --optimize --exportStart _initialize --runtime incremental \
  --exportRuntime --use abort= --transform json-as/transform); then
  echo "[build-wasm] asc compile failed" >&2
  exit 1
fi

WASM_FILE="$BUILD_DIR/build/$MODULE_NAME"
if [[ ! -s "$WASM_FILE" ]]; then
  echo "[build-wasm] ERROR: no WASM module was produced." >&2
  exit 1
fi

cp "$WASM_FILE" "$MODULES_DIR/$MODULE_NAME"
log "module: ${MODULES_DIR#"$ROOT"/}/$MODULE_NAME ($(wc -c < "$MODULES_DIR/$MODULE_NAME") bytes)"

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

log "manifest: ${CONFIG_DIR#"$ROOT"/}/sekiban-manifest.json"
log "OK"
