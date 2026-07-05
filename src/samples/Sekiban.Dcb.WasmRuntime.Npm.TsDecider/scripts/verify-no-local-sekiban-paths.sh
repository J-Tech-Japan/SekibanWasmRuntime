#!/usr/bin/env bash
# External-consumer dependency guard for the npm TypeScript published-package
# sample. The committed Wasm/package.json and Client/package.json must depend
# on @sekiban/as-wasm / @sekiban/ts at exact registry versions only -- no
# file:, link:, or relative-path references into src/lib. Any npm lockfile
# present under the sample (package-lock.json, npm-shrinkwrap.json) is also
# scanned: a tarball install that leaked a local path into a committed
# lockfile would otherwise pass the manifest-only check silently.
#
# Unlike the crates.io Rust guard (which runs a live `cargo check` against the
# already-published crates.io crates), this guard is static: @sekiban/ts and
# @sekiban/as-wasm are not published to npm yet (SWR-G058), so a real
# `npm install` against the registry would fail here regardless of the
# sample's correctness. The live build-and-run proof runs through
# scripts/build-wasm.sh and scripts/smoke.sh with SEKIBAN_NPM_MODE=tarball,
# which installs from locally packed tarballs instead of the registry.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider"
command -v node >/dev/null 2>&1 || { echo "node not found; required to inspect npm lockfiles" >&2; exit 1; }

for manifest in "$SAMPLE_DIR/Wasm/package.json" "$SAMPLE_DIR/Client/package.json"; do
  if [[ ! -f "$manifest" ]]; then
    echo "missing manifest: $manifest" >&2
    exit 1
  fi
  if grep -Eq '"@sekiban/(ts|as-wasm)"[[:space:]]*:[[:space:]]*"(file:|link:|\.\./|\./)' "$manifest"; then
    echo "forbidden local Sekiban path dependency found in $manifest" >&2
    exit 1
  fi
done

for lockfile in \
  "$SAMPLE_DIR/Wasm/package-lock.json" "$SAMPLE_DIR/Wasm/npm-shrinkwrap.json" \
  "$SAMPLE_DIR/Client/package-lock.json" "$SAMPLE_DIR/Client/npm-shrinkwrap.json"
do
  [[ -f "$lockfile" ]] || continue
  if ! node -e "
    const fs = require('fs');
    const lockPath = process.argv[1];
    const data = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
    const isLocal = (resolved) =>
      typeof resolved === 'string' &&
      (/^file:/.test(resolved) || /^link:/.test(resolved) || resolved.includes('../') || resolved.includes('src/lib'));
    const bad = [];

    // npm lockfile v2/v3 schema
    if (data.packages) {
      for (const [key, val] of Object.entries(data.packages)) {
        if (/node_modules\/@sekiban\/(ts|as-wasm)\$/.test(key) && isLocal(val && val.resolved)) {
          bad.push(key + ' -> ' + val.resolved);
        }
      }
    }
    // npm lockfile v1 schema
    if (data.dependencies) {
      for (const [name, val] of Object.entries(data.dependencies)) {
        if ((name === '@sekiban/ts' || name === '@sekiban/as-wasm') && isLocal(val && val.resolved)) {
          bad.push(name + ' -> ' + val.resolved);
        }
      }
    }

    if (bad.length > 0) {
      console.error('forbidden local Sekiban path reference(s) in ' + lockPath + ': ' + bad.join(', '));
      process.exit(1);
    }
  " "$lockfile"; then
    exit 1
  fi
done

if ! grep -Eq '"@sekiban/as-wasm"[[:space:]]*:[[:space:]]*"0\.1\.0"' "$SAMPLE_DIR/Wasm/package.json"; then
  echo "$SAMPLE_DIR/Wasm/package.json must depend on @sekiban/as-wasm at exact version 0.1.0" >&2
  exit 1
fi
if ! grep -Eq '"@sekiban/ts"[[:space:]]*:[[:space:]]*"0\.1\.0"' "$SAMPLE_DIR/Client/package.json"; then
  echo "$SAMPLE_DIR/Client/package.json must depend on @sekiban/ts at exact version 0.1.0" >&2
  exit 1
fi

# The end-to-end smoke must target the public GHCR runtime image, not a locally
# built runtime, so the sample proves published artifacts only.
APPHOST_PROGRAM="$SAMPLE_DIR/AppHost/Program.cs"
if [[ ! -f "$APPHOST_PROGRAM" ]]; then
  echo "missing AppHost Program.cs for the public GHCR runtime orchestration" >&2
  exit 1
fi
if ! grep -q 'ghcr\.io/j-tech-japan/sekiban-wasm-runtime-host' "$APPHOST_PROGRAM"; then
  echo "AppHost must target the public GHCR runtime image ghcr.io/j-tech-japan/sekiban-wasm-runtime-host" >&2
  exit 1
fi

echo "npm TypeScript sample dependency guard passed"
