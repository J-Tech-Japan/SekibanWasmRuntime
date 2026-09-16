#!/usr/bin/env bash
# External-consumer dependency guard for the npm TypeScript sample.
# Client must pin exact public @sekiban/dcb-* 0.1.0 packages and zod 4.4.3.
# Wasm keeps @sekiban/as-wasm at exact 0.1.0 with no local path deps.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider"
command -v node >/dev/null 2>&1 || { echo "node not found; required to inspect npm lockfiles" >&2; exit 1; }

for manifest in "$SAMPLE_DIR/Wasm/package.json" "$SAMPLE_DIR/Client/package.json"; do
  [[ -f "$manifest" ]] || { echo "missing manifest: $manifest" >&2; exit 1; }
  if grep -q '@sekiban/ts' "$manifest"; then
    echo "retired @sekiban/ts dependency found in $manifest" >&2
    exit 1
  fi
  if grep -Eq '"@sekiban/(as-wasm|dcb-core|dcb-domain|dcb-client)"[[:space:]]*:[[:space:]]*"(file:|link:|\.\./|\./)' "$manifest"; then
    echo "forbidden local Sekiban path dependency found in $manifest" >&2
    exit 1
  fi
done

for lockfile in \
  "$SAMPLE_DIR/Wasm/package-lock.json" "$SAMPLE_DIR/Wasm/npm-shrinkwrap.json" \
  "$SAMPLE_DIR/Client/package-lock.json" "$SAMPLE_DIR/Client/npm-shrinkwrap.json"
do
  [[ -f "$lockfile" ]] || continue
  node -e "
    const fs = require('fs');
    const lockPath = process.argv[1];
    const data = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
    const isLocal = (resolved) =>
      typeof resolved === 'string' &&
      (/^file:/.test(resolved) || /^link:/.test(resolved) || resolved.includes('../') || resolved.includes('src/lib'));
    const bad = [];
    const names = ['@sekiban/as-wasm', '@sekiban/dcb-core', '@sekiban/dcb-domain', '@sekiban/dcb-client', '@sekiban/ts'];
    if (data.packages) {
      for (const [key, val] of Object.entries(data.packages)) {
        if (names.some((name) => key.endsWith('node_modules/' + name)) && isLocal(val && val.resolved)) {
          bad.push(key + ' -> ' + val.resolved);
        }
      }
    }
    if (bad.length > 0) {
      console.error('forbidden local Sekiban path reference(s) in ' + lockPath + ': ' + bad.join(', '));
      process.exit(1);
    }
  " "$lockfile"
done

grep -Eq '"@sekiban/as-wasm"[[:space:]]*:[[:space:]]*"0\.1\.0"' "$SAMPLE_DIR/Wasm/package.json" \
  || { echo "Wasm must depend on @sekiban/as-wasm@0.1.0" >&2; exit 1; }
for pkg in dcb-core dcb-domain dcb-client; do
  grep -Eq "\"@sekiban/${pkg}\"[[:space:]]*:[[:space:]]*\"0\\.1\\.0\"" "$SAMPLE_DIR/Client/package.json" \
    || { echo "Client must depend on @sekiban/${pkg}@0.1.0" >&2; exit 1; }
done
grep -Eq '"zod"[[:space:]]*:[[:space:]]*"4\.4\.3"' "$SAMPLE_DIR/Client/package.json" \
  || { echo "Client must pin zod@4.4.3" >&2; exit 1; }

APPHOST_PROGRAM="$SAMPLE_DIR/AppHost/Program.cs"
grep -q 'ghcr\.io/j-tech-japan/sekiban-wasm-runtime-host' "$APPHOST_PROGRAM" \
  || { echo "AppHost must target the public GHCR runtime image" >&2; exit 1; }

echo "npm TypeScript sample dependency guard passed"
