#!/usr/bin/env bash
# Registry-backed npm extraction smoke for @sekiban/as-wasm and published DCB clients.
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"
ROOT="$(pwd)"

SMOKE_ROOT="${NPM_EXTRACTION_SMOKE_DIR:-$ROOT/artifacts/npm-extraction-smoke}"
REPORT_DIR="${RELEASE_REPORT_DIR:-$ROOT/artifacts/release}"
REPORT="$REPORT_DIR/npm-extraction-smoke.md"
RUNTIME_IMAGE="ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:${SAMPLE_RUNTIME_IMAGE_TAG:-1.0.0-preview.3}"
READY_TIMEOUT="${NPM_EXTRACTION_SMOKE_TIMEOUT:-180}"

AS_PKG_DIR="$ROOT/src/lib/sekiban-as-wasm"
SMALL_CLIENT="$ROOT/src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/Client"
TS_WASM_DIR="$ROOT/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/ts-wasm"

CONTAINER_ID=""
PG_CONTAINER_ID=""
DOCKER_NETWORK=""
CONTAINER_RESULT="not-run"
CONTAINER_DETAIL=""

log() { printf '[npm-extraction-smoke] %s\n' "$*"; }

write_report() {
  local result="$1" detail="$2"
  mkdir -p "$REPORT_DIR"
  {
    printf '# npm Extraction Smoke\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Packages: \`@sekiban/as-wasm@$AS_VERSION\` (packed tarball) + \`@sekiban/dcb-core/domain/client@0.2.0\` (registry)"
    printf '%s\n' "- Runtime image: \`$RUNTIME_IMAGE\`"
    printf '%s\n' "- Container load: $CONTAINER_RESULT${CONTAINER_DETAIL:+ — $CONTAINER_DETAIL}"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
  } > "$REPORT"
  log "report: ${REPORT#"$ROOT"/}"
}

cleanup() {
  [[ -n "$CONTAINER_ID" ]] && docker rm -f "$CONTAINER_ID" >/dev/null 2>&1 || true
  [[ -n "$PG_CONTAINER_ID" ]] && docker rm -f "$PG_CONTAINER_ID" >/dev/null 2>&1 || true
  [[ -n "$DOCKER_NETWORK" ]] && docker network rm "$DOCKER_NETWORK" >/dev/null 2>&1 || true
}
trap cleanup EXIT

fail() { log "FAIL: $*"; write_report "FAIL" "$*"; exit 1; }

command -v npm >/dev/null 2>&1 || fail "npm not found"
command -v node >/dev/null 2>&1 || fail "node not found"

AS_VERSION="$(node -p "require('$AS_PKG_DIR/package.json').version")"
rm -rf "$SMOKE_ROOT"
mkdir -p "$SMOKE_ROOT" "$REPORT_DIR"

log "packing @sekiban/as-wasm@$AS_VERSION"
npm --prefix "$AS_PKG_DIR" install --no-audit --no-fund >/dev/null 2>&1 || fail "npm install failed for @sekiban/as-wasm"
AS_TGZ_NAME="$(cd "$AS_PKG_DIR" && npm pack --pack-destination "$SMOKE_ROOT" --silent 2>/dev/null | tail -n1)"
AS_TGZ="$SMOKE_ROOT/$AS_TGZ_NAME"
[[ -s "$AS_TGZ" ]] || fail "npm pack produced no tarball for @sekiban/as-wasm"

PROJ_DIR="$SMOKE_ROOT/projector-consumer"
mkdir -p "$PROJ_DIR/modules"
cp -R "$TS_WASM_DIR/assembly" "$PROJ_DIR/assembly"
cp "$TS_WASM_DIR/tsconfig.json" "$PROJ_DIR/tsconfig.json"
node -e "
  const fs = require('fs');
  fs.writeFileSync('$PROJ_DIR/package.json', JSON.stringify({
    name: 'projector-consumer-smoke', private: true, type: 'module',
    dependencies: { '@sekiban/as-wasm': 'file:$AS_TGZ' },
    devDependencies: { typescript: '^5.7.3', '@types/node': '^22.10.5', 'assemblyscript': '^0.27.0' }
  }, null, 2));
"
(cd "$PROJ_DIR" && npm install --no-audit --no-fund >/dev/null 2>&1) || fail "projector consumer npm install failed"

log "registry install + build of migrated small TypeScript client"
CLIENT_SMOKE_DIR="$SMOKE_ROOT/registry-client"
rm -rf "$CLIENT_SMOKE_DIR"
mkdir -p "$CLIENT_SMOKE_DIR"
cp -R "$SMALL_CLIENT/src" "$CLIENT_SMOKE_DIR/src"
cp "$SMALL_CLIENT/tsconfig.json" "$CLIENT_SMOKE_DIR/tsconfig.json"
cp "$SMALL_CLIENT/package.json" "$CLIENT_SMOKE_DIR/package.json"
(cd "$CLIENT_SMOKE_DIR" && npm install --no-audit --no-fund >/dev/null 2>&1) || fail "registry npm install failed for DCB client"
for pkg in @sekiban/dcb-core @sekiban/dcb-domain @sekiban/dcb-client; do
  resolved="$(node -p "require('$CLIENT_SMOKE_DIR/package-lock.json').packages['node_modules/${pkg}'].version || ''")"
  [[ "$resolved" == "0.2.0" ]] || fail "$pkg resolved to '$resolved' instead of registry 0.2.0"
done
(cd "$CLIENT_SMOKE_DIR" && npm run build && npm test) || fail "registry-backed client build/test failed"

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  CONTAINER_RESULT="skipped-runtime"
  CONTAINER_DETAIL="registry client build/test passed; full container runtime smoke runs in sample scripts/smoke.sh"
else
  CONTAINER_RESULT="SKIPPED"
  CONTAINER_DETAIL="Docker unavailable"
fi

write_report "PASS" "Packed @sekiban/as-wasm projector consumer and registry @sekiban/dcb-* 0.2.0 client build/test succeeded."
exit 0
