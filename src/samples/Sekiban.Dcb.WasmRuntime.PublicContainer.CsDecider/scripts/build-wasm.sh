#!/usr/bin/env bash
#
# Build the sample's C# Decider domain to a runtime-loadable WASM module and
# generate the runtime manifest, into a stable artifact path. Run before the
# AppHost so the container has a module + manifest to mount.
#
# Outputs (git-ignored):
#   artifacts/samples/public-container-cs-decider/modules/public-container-cs-decider.wasm
#   artifacts/samples/public-container-cs-decider/config/sekiban-manifest.json
#
# The WASM module is a NativeAOT-LLVM wasi-wasm reactor (Docker linux/amd64 on
# non-Linux hosts, matching build/scripts/build-csharp-wasm.sh). It is NOT
# checked in; regenerate any time with this script.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

WASM_PROJ_REL="src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/Wasm/PublicContainerCsDecider.Wasm.csproj"
NUGET_WASM_CONFIG_REL="NuGet.wasm.config"
ARTIFACT_DIR="artifacts/samples/public-container-cs-decider"
PUBLISH_DIR_REL="$ARTIFACT_DIR/publish"
MODULES_DIR="$ROOT/$ARTIFACT_DIR/modules"
CONFIG_DIR="$ROOT/$ARTIFACT_DIR/config"
MODULE_NAME="public-container-cs-decider.wasm"
DOTNET_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"

HOST_OS="$(uname -s)"
if [[ "${BUILD_WASM_MODE:-}" == "docker" ]]; then BUILD_MODE="docker"
elif [[ "${BUILD_WASM_MODE:-}" == "native" ]]; then BUILD_MODE="native"
elif [[ "${CI:-}" == "true" ]]; then BUILD_MODE="docker"
elif [[ "$HOST_OS" == "Linux" ]]; then BUILD_MODE="native"
else BUILD_MODE="docker"; fi

rm -rf "$ROOT/$PUBLISH_DIR_REL"
mkdir -p "$ROOT/$PUBLISH_DIR_REL" "$MODULES_DIR" "$CONFIG_DIR"

echo "[build-wasm] host=$HOST_OS mode=$BUILD_MODE project=$WASM_PROJ_REL"

publish_native() {
  EnableMacIlCompilerRuntime=true dotnet publish "$WASM_PROJ_REL" -c Release -r wasi-wasm \
    -o "$ROOT/$PUBLISH_DIR_REL" --configfile "$NUGET_WASM_CONFIG_REL"
}

publish_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    echo "[build-wasm] ERROR: Docker is required on non-Linux hosts but was not found." >&2
    echo "[build-wasm] Install Docker Desktop, or run on Linux with the WASI SDK." >&2
    exit 1
  fi
  local wasi_sdk_version=29
  local wasi_sdk_url="https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-${wasi_sdk_version}/wasi-sdk-${wasi_sdk_version}.0-x86_64-linux.tar.gz"
  docker run --rm --platform linux/amd64 -v "$ROOT":/work -w /work "$DOTNET_IMAGE" bash -c "
    set -euo pipefail
    curl -sSfL '${wasi_sdk_url}' | tar xz -C /opt
    ln -sf /opt/wasi-sdk-${wasi_sdk_version}.0-x86_64-linux /opt/wasi-sdk
    dotnet publish ${WASM_PROJ_REL} -c Release -r wasi-wasm -o ${PUBLISH_DIR_REL} \
      --configfile ${NUGET_WASM_CONFIG_REL}
  "
}

if [[ "$BUILD_MODE" == "native" ]]; then publish_native; else publish_docker; fi

WASM_FILE="$(find "$ROOT/$PUBLISH_DIR_REL" -name '*.wasm' -type f | head -n 1)"
if [[ -z "$WASM_FILE" || ! -f "$WASM_FILE" ]]; then
  WASM_FILE="$(find "$ROOT/src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/Wasm/bin" -name '*.wasm' -type f 2>/dev/null | head -n 1)"
fi
if [[ -z "$WASM_FILE" || ! -f "$WASM_FILE" ]]; then
  echo "[build-wasm] ERROR: no .wasm produced. Publish output:" >&2
  ls -la "$ROOT/$PUBLISH_DIR_REL" >&2 || true
  exit 1
fi

cp "$WASM_FILE" "$MODULES_DIR/$MODULE_NAME"
echo "[build-wasm] module: $ARTIFACT_DIR/modules/$MODULE_NAME ($(wc -c < "$MODULES_DIR/$MODULE_NAME") bytes)"

# The runtime instantiates the core module embedded in a WASI component. Hash the same effective
# bytes that Wasmtime receives rather than the outer component container. Core-module guests are
# hashed directly. `wasm-tools` is intentionally only required for this component artifact path;
# without it, fail closed instead of publishing a manifest that can never pass the runtime gate.
effective_module_sha256() {
  local module_path="$1"
  local header
  header="$(od -An -tx1 -N8 "$module_path" | tr -d ' \n')"
  if [[ "$header" != "0061736d0d000100" ]]; then
    sha256sum "$module_path" | awk '{print $1}'
    return 0
  fi

  command -v wasm-tools >/dev/null 2>&1 || {
    echo "[build-wasm] ERROR: wasm-tools is required to hash the instantiated core module" >&2
    return 1
  }

  local extract_dir
  extract_dir="$(mktemp -d "${TMPDIR:-/tmp}/swr-g079-component.XXXXXX")"
  local module_dir="$extract_dir/modules"
  mkdir -p "$module_dir"
  if ! wasm-tools component unbundle "$module_path" \
      --module-dir "$module_dir" -o "$extract_dir/component.wasm" >/dev/null; then
    rm -rf "$extract_dir"
    echo "[build-wasm] ERROR: could not extract the instantiated core module" >&2
    return 1
  fi

  local core_module_count=0
  local core_module_path=""
  while IFS= read -r candidate; do
    core_module_count=$((core_module_count + 1))
    core_module_path="$candidate"
  done < <(find "$module_dir" -maxdepth 1 -name 'unbundled-module*.wasm' -type f | sort)
  if [[ "$core_module_count" -ne 1 ]]; then
    rm -rf "$extract_dir"
    echo "[build-wasm] ERROR: expected exactly one embedded core module, found $core_module_count" >&2
    return 1
  fi

  local digest
  digest="$(sha256sum "$core_module_path" | awk '{print $1}')"
  rm -rf "$extract_dir"
  printf '%s' "$digest"
}

MODULE_SHA256="$(effective_module_sha256 "$MODULES_DIR/$MODULE_NAME")"

# Runtime manifest for the weather Decider domain (mounted into the container).
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
      "projectorVersion": "v1"
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
