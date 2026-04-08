#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

WASM_PROJ="$ROOT/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.Wasm/SekibanDcbDecider.Wasm.csproj"
PUBLISH_DIR="$ROOT/artifacts/sekiban-dcb-decider-wasm"
MODULE_PATH="$ROOT/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/modules/sekiban-dcb-decider.wasm"
EXPECTED_WASM_NAME="SekibanDcbDecider.Wasm.wasm"
NUGET_WASM_CONFIG="$ROOT/NuGet.wasm.config"

WASM_PROJ_REL="src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.Wasm/SekibanDcbDecider.Wasm.csproj"
PUBLISH_DIR_REL="artifacts/sekiban-dcb-decider-wasm"
NUGET_WASM_CONFIG_REL="NuGet.wasm.config"
DOTNET_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
REQUIRED_SDK_PREFIX="10.0."

HOST_OS="$(uname -s)"
if [[ "${BUILD_CSHARP_WASM_MODE:-}" == "docker" ]]; then
  BUILD_MODE="docker"
elif [[ "${BUILD_CSHARP_WASM_MODE:-}" == "native" ]]; then
  BUILD_MODE="native"
elif [[ "${CI:-}" == "true" ]]; then
  BUILD_MODE="docker"
elif [[ "$HOST_OS" == "Linux" ]]; then
  BUILD_MODE="native"
else
  BUILD_MODE="docker"
fi

mkdir -p "$PUBLISH_DIR" "$(dirname "$MODULE_PATH")"

publish_native() {
  dotnet publish "$WASM_PROJ" -c Release -r wasi-wasm -o "$PUBLISH_DIR" \
    --configfile "$NUGET_WASM_CONFIG"
}

publish_docker() {
  if ! command -v docker &>/dev/null; then
    echo "[build-sample-csharp-wasm] ERROR: Docker is required on non-Linux hosts but was not found." >&2
    exit 1
  fi

  local wasi_sdk_version=29
  local wasi_sdk_url="https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-${wasi_sdk_version}/wasi-sdk-${wasi_sdk_version}.0-x86_64-linux.tar.gz"

  docker run --rm \
    --platform linux/amd64 \
    -v "$ROOT":/work \
    -w /work \
    "$DOTNET_IMAGE" \
    bash -c "
      set -euo pipefail
      dotnet --list-sdks | grep -q '^${REQUIRED_SDK_PREFIX}' || {
        echo 'ERROR: .NET SDK ${REQUIRED_SDK_PREFIX} not found in container' >&2
        dotnet --list-sdks >&2
        exit 1
      }
      curl -sSfL '${wasi_sdk_url}' | tar xz -C /opt
      ln -sf /opt/wasi-sdk-${wasi_sdk_version}.0-x86_64-linux /opt/wasi-sdk
      dotnet publish ${WASM_PROJ_REL} -c Release -r wasi-wasm -o ${PUBLISH_DIR_REL} \
        --configfile ${NUGET_WASM_CONFIG_REL}
    "
}

echo "[build-sample-csharp-wasm] host OS:     $HOST_OS"
echo "[build-sample-csharp-wasm] build mode:  $BUILD_MODE"
echo "[build-sample-csharp-wasm] project:     $WASM_PROJ"
echo "[build-sample-csharp-wasm] publish-dir: $PUBLISH_DIR"

if [[ "$BUILD_MODE" == "native" ]]; then
  publish_native
else
  publish_docker
fi

WASM_FILE="$PUBLISH_DIR/$EXPECTED_WASM_NAME"
if [[ ! -f "$WASM_FILE" ]]; then
  WASM_FILE="$(find "$PUBLISH_DIR" -name '*.wasm' -type f | head -n 1)"
fi

if [[ -z "$WASM_FILE" || ! -f "$WASM_FILE" ]]; then
  echo "[build-sample-csharp-wasm] ERROR: No .wasm file found in $PUBLISH_DIR" >&2
  exit 1
fi

cp "$WASM_FILE" "$MODULE_PATH"
echo "[build-sample-csharp-wasm] built: $MODULE_PATH ($(wc -c < "$MODULE_PATH") bytes)"
