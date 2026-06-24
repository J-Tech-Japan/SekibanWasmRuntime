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
  }
}
JSON

echo "[build-wasm] manifest: $ARTIFACT_DIR/config/sekiban-manifest.json"
echo "[build-wasm] OK"
