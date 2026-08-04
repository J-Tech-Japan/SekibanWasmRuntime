#!/usr/bin/env bash
# External-consumer dependency guard for the Swift SPM sample. The committed
# Package.swift must consume the Sekiban Swift SDK exactly as an external SPM
# user would: the public repository-root URL at an exact version — no
# .package(path:) dependencies and no local Sekiban path references. (The
# --local-package smoke mode redirects the URL to an ephemeral local Git
# repository, which never touches this manifest.)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider"
MANIFEST="$SAMPLE_DIR/Package.swift"

if rg -n '\.package\(\s*(name:[^,]+,\s*)?path:' "$MANIFEST"; then
  echo "forbidden .package(path:) dependency found in committed Package.swift" >&2
  exit 1
fi

# Local URL schemes are the same boundary violation as path dependencies: a
# `.package(url: "file:///…")` (or a bare absolute/relative filesystem URL)
# would consume a local checkout while still looking like a URL dependency.
if rg -n 'file://' "$MANIFEST"; then
  echo "forbidden local file:// dependency URL found in committed Package.swift" >&2
  exit 1
fi
if rg -n '\.package\(\s*url:\s*"(/|\.)' "$MANIFEST"; then
  echo "forbidden filesystem dependency URL found in committed Package.swift" >&2
  exit 1
fi

if rg -n 'wasm-projectors/swift|\.\./' "$MANIFEST"; then
  echo "forbidden local Sekiban path reference found in committed Package.swift" >&2
  exit 1
fi

# The exact-version pin must sit on the root-package dependency declaration
# itself (a `from:`/`branch:` drift there must fail even if some other
# dependency happens to use `exact:`).
if ! rg -Uq '\.package\(\s*name:\s*"sekiban-swift"\s*,\s*url:\s*"https://github\.com/J-Tech-Japan/SekibanWasmRuntime"\s*,\s*exact:\s*"1\.0\.0-preview\.4"\s*\)' "$MANIFEST"; then
  echo "Package.swift must depend on the SekibanWasmRuntime root package pinned at exact: \"1.0.0-preview.4\"" >&2
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
