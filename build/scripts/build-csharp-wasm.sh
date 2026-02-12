#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

WASM_PROJ="$ROOT/src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj"
PUBLISH_DIR="$ROOT/artifacts/csharp-wasm"
MODULES_DIR="$ROOT/src/internalUsage/modules"
EXPECTED_WASM_NAME="SekibanWasm.Wasm.wasm"

# Relative paths for use inside Docker container (mounted at /work)
WASM_PROJ_REL="src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj"
PUBLISH_DIR_REL="artifacts/csharp-wasm"

HOST_OS="$(uname -s)"
if [[ "$HOST_OS" == "Linux" ]]; then
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
  dotnet publish "$WASM_PROJ" -c Release -r wasi-wasm -o "$PUBLISH_DIR"
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
    -v "$ROOT":/work \
    -w /work \
    mcr.microsoft.com/dotnet/sdk:10.0-preview \
    bash -c "
      set -euo pipefail
      curl -sSfL '${wasi_sdk_url}' | tar xz -C /opt
      ln -sf /opt/wasi-sdk-${wasi_sdk_version}.0-x86_64-linux /opt/wasi-sdk
      dotnet publish ${WASM_PROJ_REL} -c Release -r wasi-wasm -o ${PUBLISH_DIR_REL}
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
