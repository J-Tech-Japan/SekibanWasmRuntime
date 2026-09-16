#!/usr/bin/env bash
# External-consumer dependency guard for the MoonBit mooncakes sample. The
# committed moon.mod.json manifests must consume the Sekiban MoonBit SDK
# exactly as an external mooncakes.io user would: registry coordinates only —
# no local path resolution. (The --local-packages smoke mode rewrites deps in
# a STAGED COPY under artifacts/, never in these committed manifests.)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

search() {
  local pattern="$1" file="$2"
  if command -v rg >/dev/null 2>&1; then
    rg -n "$pattern" "$file"
  else
    grep -En "$pattern" "$file"
  fi
}

search_quiet() {
  local pattern="$1" file="$2"
  if command -v rg >/dev/null 2>&1; then
    rg -q "$pattern" "$file"
  else
    grep -Eq "$pattern" "$file"
  fi
}

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.Mooncakes.MbDecider"

for manifest in "$SAMPLE_DIR/wasm/moon.mod.json" "$SAMPLE_DIR/client/moon.mod.json"; do
  if [[ ! -f "$manifest" ]]; then
    echo "missing manifest: $manifest" >&2
    exit 1
  fi
  if search '"path"[[:space:]]*:' "$manifest"; then
    echo "forbidden local path dependency found in committed $manifest" >&2
    exit 1
  fi
done

if ! search_quiet '"sekiban/sekiban-wasm-runtime"[[:space:]]*:[[:space:]]*"[0-9]+\.[0-9]+\.[0-9]+"' "$SAMPLE_DIR/wasm/moon.mod.json"; then
  echo "wasm/moon.mod.json must declare sekiban/sekiban-wasm-runtime as a registry version dependency" >&2
  exit 1
fi

if ! search_quiet '"sekiban/sekiban-client"[[:space:]]*:[[:space:]]*"[0-9]+\.[0-9]+\.[0-9]+"' "$SAMPLE_DIR/client/moon.mod.json"; then
  echo "client/moon.mod.json must declare sekiban/sekiban-client as a registry version dependency" >&2
  exit 1
fi

# The root workspace file must only reference the sample's own module
# subdirectories, never the in-repo SDK.
if [[ -f "$SAMPLE_DIR/moon.work.json" ]] && search 'sekiban-moonbit|\.\./' "$SAMPLE_DIR/moon.work.json"; then
  echo "forbidden local Sekiban reference found in moon.work.json" >&2
  exit 1
fi

# The end-to-end smoke must target the public GHCR runtime image, not a locally
# built runtime, so the sample proves published artifacts only.
APPHOST_PROGRAM="$SAMPLE_DIR/AppHost/Program.cs"
if [[ ! -f "$APPHOST_PROGRAM" ]]; then
  echo "missing AppHost Program.cs for the public GHCR runtime orchestration" >&2
  exit 1
fi
if ! search_quiet 'ghcr\.io/j-tech-japan/sekiban-wasm-runtime-host' "$APPHOST_PROGRAM"; then
  echo "AppHost must target the public GHCR runtime image ghcr.io/j-tech-japan/sekiban-wasm-runtime-host" >&2
  exit 1
fi

echo "MoonBit mooncakes sample dependency guard passed"
