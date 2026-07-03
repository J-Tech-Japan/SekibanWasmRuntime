#!/usr/bin/env bash
# External-consumer dependency guard for the Swift SPM sample. The committed
# Package.swift must consume the Sekiban Swift SDK exactly as an external SPM
# user would: the public sekiban-swift mirror URL at an exact version — no
# .package(path:) dependencies and no local Sekiban path references. (The
# --local-package smoke mode redirects the URL through SwiftPM's dependency
# mirroring, which never touches this manifest.)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider"
MANIFEST="$SAMPLE_DIR/Package.swift"

if rg -n '\.package\(\s*(name:[^,]+,\s*)?path:' "$MANIFEST"; then
  echo "forbidden .package(path:) dependency found in committed Package.swift" >&2
  exit 1
fi

if rg -n 'wasm-projectors/swift|\.\./' "$MANIFEST"; then
  echo "forbidden local Sekiban path reference found in committed Package.swift" >&2
  exit 1
fi

if ! rg -q 'https://github\.com/J-Tech-Japan/sekiban-swift' "$MANIFEST"; then
  echo "Package.swift must depend on the public mirror https://github.com/J-Tech-Japan/sekiban-swift" >&2
  exit 1
fi

if ! rg -q 'exact:\s*"' "$MANIFEST"; then
  echo "Package.swift must pin the sekiban-swift dependency to an exact version" >&2
  exit 1
fi

# The end-to-end smoke must target the public GHCR runtime image, not a locally
# built runtime, so the sample proves published artifacts only.
APPHOST_PROGRAM="$SAMPLE_DIR/AppHost/Program.cs"
if [[ ! -f "$APPHOST_PROGRAM" ]]; then
  echo "missing AppHost Program.cs for the public GHCR runtime orchestration" >&2
  exit 1
fi
if ! rg -q 'ghcr\.io/j-tech-japan/sekiban-wasm-runtime-host' "$APPHOST_PROGRAM"; then
  echo "AppHost must target the public GHCR runtime image ghcr.io/j-tech-japan/sekiban-wasm-runtime-host" >&2
  exit 1
fi

echo "Swift SPM sample dependency guard passed"
