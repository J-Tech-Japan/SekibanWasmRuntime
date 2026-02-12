#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

WASM_PROJ="src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj"
WASM_PROJ_ABS="$ROOT/$WASM_PROJ"
PUBLISH_DIR="artifacts/csharp-wasm"
PUBLISH_DIR_ABS="$ROOT/$PUBLISH_DIR"
MODULES_DIR="$ROOT/src/internalUsage/modules"
EXPECTED_WASM_NAME="SekibanWasm.Wasm.wasm"
WASI_SDK_VERSION=29

HOST_OS="$(uname -s)"

if [[ "$HOST_OS" == "Linux" ]]; then
  BUILD_MODE="native"
else
  BUILD_MODE="docker"
fi

echo "[build-csharp-wasm] host OS:     $HOST_OS"
echo "[build-csharp-wasm] build mode:  $BUILD_MODE"
echo "[build-csharp-wasm] project:     $WASM_PROJ_ABS"
echo "[build-csharp-wasm] publish-dir: $PUBLISH_DIR_ABS"

mkdir -p "$PUBLISH_DIR_ABS" "$MODULES_DIR"

echo "[build-csharp-wasm] Publishing C# WASM module..."

if [[ "$BUILD_MODE" == "docker" ]]; then
  if ! command -v docker &>/dev/null; then
    echo "[build-csharp-wasm] ERROR: Docker is required on non-Linux hosts but was not found." >&2
    echo "[build-csharp-wasm] Install Docker Desktop and try again." >&2
    exit 1
  fi

  # Build inside a Linux container so the Linux ILCompiler runtime is used
  if ! docker run --rm \
    --platform linux/amd64 \
    -v "$ROOT":/work \
    -w /work \
    mcr.microsoft.com/dotnet/sdk:10.0-preview \
    bash -c "
      set -euo pipefail
      WASI_SDK_VERSION=${WASI_SDK_VERSION}
      echo \"[build-csharp-wasm/docker] Installing WASI SDK \${WASI_SDK_VERSION}...\"
      curl -sSfL \"https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-\${WASI_SDK_VERSION}/wasi-sdk-\${WASI_SDK_VERSION}.0-x86_64-linux.tar.gz\" | tar xz -C /opt
      ln -sf /opt/wasi-sdk-\${WASI_SDK_VERSION}.0-x86_64-linux /opt/wasi-sdk
      echo '[build-csharp-wasm/docker] Running dotnet publish...'
      dotnet publish ${WASM_PROJ} -c Release -r wasi-wasm -o ${PUBLISH_DIR}
    "; then
    echo "[build-csharp-wasm] ERROR: dotnet publish failed inside Docker container." >&2
    exit 1
  fi
else
  if ! dotnet publish "$WASM_PROJ_ABS" -c Release -r wasi-wasm -o "$PUBLISH_DIR_ABS"; then
    echo "[build-csharp-wasm] ERROR: dotnet publish failed." >&2
    echo "[build-csharp-wasm] Check the following in SekibanWasm.Wasm.csproj:" >&2
    echo "[build-csharp-wasm]   - IlcTrimMetadata should be false" >&2
    echo "[build-csharp-wasm]   - RuntimeIdentifier should be wasi-wasm" >&2
    exit 1
  fi
fi

echo "[build-csharp-wasm] Publish succeeded. Scanning for .wasm output..."

WASM_FILE="$PUBLISH_DIR_ABS/$EXPECTED_WASM_NAME"
if [[ ! -f "$WASM_FILE" ]]; then
  echo "[build-csharp-wasm] Expected $EXPECTED_WASM_NAME not found; searching for any .wasm file..." >&2
  WASM_FILE=$(find "$PUBLISH_DIR_ABS" -maxdepth 1 -name '*.wasm' -type f | head -n 1)
fi

if [[ -z "$WASM_FILE" || ! -f "$WASM_FILE" ]]; then
  echo "[build-csharp-wasm] ERROR: No .wasm file found in $PUBLISH_DIR_ABS" >&2
  echo "[build-csharp-wasm] Contents of publish directory:" >&2
  ls -la "$PUBLISH_DIR_ABS" >&2
  exit 1
fi

cp "$WASM_FILE" "$MODULES_DIR/csharp-weather.wasm"
echo "[build-csharp-wasm] built: $MODULES_DIR/csharp-weather.wasm ($(wc -c < "$MODULES_DIR/csharp-weather.wasm") bytes)"
