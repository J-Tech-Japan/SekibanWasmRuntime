#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

WASM_PROJ="$ROOT/src/internalUsages/cs/SekibanWasm.Cs.Wasm/SekibanWasm.Cs.Wasm.csproj"
PUBLISH_DIR="$ROOT/artifacts/csharp-wasm"
MODULES_DIR="$ROOT/src/internalUsages/cs/modules"
EXPECTED_WASM_NAME="SekibanWasm.Cs.Wasm.wasm"
NUGET_WASM_CONFIG="$ROOT/NuGet.wasm.config"

# Relative paths for use inside Docker container (mounted at /work)
WASM_PROJ_REL="src/internalUsages/cs/SekibanWasm.Cs.Wasm/SekibanWasm.Cs.Wasm.csproj"
PUBLISH_DIR_REL="artifacts/csharp-wasm"
NUGET_WASM_CONFIG_REL="NuGet.wasm.config"
DOTNET_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
REQUIRED_SDK_PREFIX="10.0.1"

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

mkdir -p "$PUBLISH_DIR" "$MODULES_DIR"

echo "[build-csharp-wasm] host OS:     $HOST_OS"
echo "[build-csharp-wasm] build mode:  $BUILD_MODE"
echo "[build-csharp-wasm] project:     $WASM_PROJ"
echo "[build-csharp-wasm] publish-dir: $PUBLISH_DIR"

publish_native() {
  dotnet publish "$WASM_PROJ" -c Release -r wasi-wasm -o "$PUBLISH_DIR" \
    --configfile "$NUGET_WASM_CONFIG"
}

publish_docker() {
  if ! command -v docker &>/dev/null; then
    echo "[build-csharp-wasm] ERROR: Docker is required on non-Linux hosts but was not found." >&2
    echo "[build-csharp-wasm] Install Docker Desktop: https://docs.docker.com/get-docker/" >&2
    exit 1
  fi

  # WASI SDK version and install path must match CI (.github/workflows/ci.yml)
  local wasi_sdk_version=29
  local wasi_sdk_url="https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-${wasi_sdk_version}/wasi-sdk-${wasi_sdk_version}.0-x86_64-linux.tar.gz"

  docker run --rm \
    --platform linux/amd64 \
    -v "$ROOT":/work \
    -w /work \
    "$DOTNET_IMAGE" \
    bash -c "
      set -euo pipefail
      dotnet --info
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

echo "[build-csharp-wasm] Publishing C# WASM module..."

if [[ "$BUILD_MODE" == "native" ]]; then
  if ! publish_native; then
    echo "[build-csharp-wasm] ERROR: dotnet publish failed." >&2
    exit 1
  fi
else
  if ! publish_docker; then
    echo "[build-csharp-wasm] ERROR: Docker-based dotnet publish failed." >&2
    exit 1
  fi
fi

echo "[build-csharp-wasm] Publish succeeded. Scanning for .wasm output..."

WASM_FILE="$PUBLISH_DIR/$EXPECTED_WASM_NAME"
if [[ ! -f "$WASM_FILE" ]]; then
  echo "[build-csharp-wasm] Expected $EXPECTED_WASM_NAME not found at publish root; searching recursively..." >&2
  WASM_FILE=$(find "$PUBLISH_DIR" -name '*.wasm' -type f | head -n 1)
fi

if [[ -z "$WASM_FILE" || ! -f "$WASM_FILE" ]]; then
  echo "[build-csharp-wasm] No .wasm found under publish dir; searching project output directories..." >&2
  WASM_FILE=$(find "$(dirname "$WASM_PROJ")/bin" -name '*.wasm' -type f | head -n 1)
fi

if [[ -z "$WASM_FILE" || ! -f "$WASM_FILE" ]]; then
  echo "[build-csharp-wasm] ERROR: No .wasm file found in $PUBLISH_DIR" >&2
  echo "[build-csharp-wasm] Contents of publish directory:" >&2
  ls -la "$PUBLISH_DIR" >&2
  exit 1
fi

cp "$WASM_FILE" "$MODULES_DIR/csharp-weather.wasm"
echo "[build-csharp-wasm] built: $MODULES_DIR/csharp-weather.wasm ($(wc -c < "$MODULES_DIR/csharp-weather.wasm") bytes)"
