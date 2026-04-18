#!/usr/bin/env bash
set -euo pipefail

# Build the Swift WASM sample's core wasm module and copy it into the sample's modules/
# directory where the Aspire AppHost expects it. Reuses the known-good toolchain layout
# documented in tasks/0417_swift_mv: swiftly-managed swift 6.3.1 with the
# swift-6.3.1-RELEASE_wasm WebAssembly SDK.

if [[ -f "$HOME/.swiftly/env.sh" ]]; then
  # shellcheck disable=SC1091
  . "$HOME/.swiftly/env.sh"
fi
hash -r

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAMPLE_DIR="$ROOT/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Swift"
PACKAGE_DIR="$SAMPLE_DIR/SekibanDcbDecider.Swift.Wasm"
MODULES_DIR="$SAMPLE_DIR/modules"
SDK="${SWIFT_WASM_SDK:-swift-6.3.1-RELEASE_wasm}"

echo "[build-swift-wasm] swift --version"
swift --version

echo "[build-swift-wasm] SwiftPM build (release, swift-sdk=$SDK)"
(
  cd "$PACKAGE_DIR"
  swift build --swift-sdk "$SDK" -c release
)

BIN_DIR="$(cd "$PACKAGE_DIR" && swift build --swift-sdk "$SDK" -c release --show-bin-path)"
SRC_WASM="$BIN_DIR/SekibanDcbDeciderSwiftWasm.wasm"
if [[ ! -f "$SRC_WASM" ]]; then
  echo "[build-swift-wasm] ERROR: expected $SRC_WASM not found" >&2
  exit 1
fi

mkdir -p "$MODULES_DIR"
cp "$SRC_WASM" "$MODULES_DIR/sekiban-dcb-decider-swift.wasm"
echo "[build-swift-wasm] copied -> $MODULES_DIR/sekiban-dcb-decider-swift.wasm"

if command -v wasm2wat >/dev/null 2>&1; then
  echo "[build-swift-wasm] exports:"
  wasm2wat "$MODULES_DIR/sekiban-dcb-decider-swift.wasm" \
    | grep -E '\(export "(memory|alloc|dealloc|mv_|create_instance|apply_event|serialize_state|restore_state|execute_query|execute_list_query)' \
    | head -20
fi
