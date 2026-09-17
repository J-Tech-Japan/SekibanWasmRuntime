#!/usr/bin/env bash
# Registry-backed npm extraction smoke for @sekiban/as-wasm and published DCB clients.
# By default the runtime container is built from this checkout's Dockerfile so CI
# exercises exact-head host code. Set RUNTIME_IMAGE explicitly (e.g. to a published
# GHCR tag) for operator/manual runs against a prebuilt image; use --from-source or
# BUILD_RUNTIME_IMAGE_FROM_SOURCE=1 to force a local rebuild even when RUNTIME_IMAGE
# is preset.
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"
ROOT="$(pwd)"

FROM_SOURCE=0
for arg in "$@"; do
  case "$arg" in
    --from-source) FROM_SOURCE=1 ;;
  esac
done

SMOKE_ROOT="${NPM_EXTRACTION_SMOKE_DIR:-$ROOT/artifacts/npm-extraction-smoke}"
REPORT_DIR="${RELEASE_REPORT_DIR:-$ROOT/artifacts/release}"
REPORT="$REPORT_DIR/npm-extraction-smoke.md"
RUNTIME_IMAGE="${RUNTIME_IMAGE:-}"
RUNTIME_IMAGE_DETAIL=""
LOCAL_SMOKE_TAG="${LOCAL_RUNTIME_IMAGE_TAG:-sekiban-wasm-runtime-host:local-smoke}"
READY_TIMEOUT="${NPM_EXTRACTION_SMOKE_TIMEOUT:-180}"
SAMPLE_ROOT="$ROOT/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts"

AS_PKG_DIR="$ROOT/src/lib/sekiban-as-wasm"
SMALL_CLIENT="$ROOT/src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/Client"
TS_WASM_DIR="$SAMPLE_ROOT/ts-wasm"

CONTAINER_ID=""
PG_CONTAINER_ID=""
DOCKER_NETWORK=""
CONTAINER_RESULT="not-run"
CONTAINER_DETAIL=""

log() { printf '[npm-extraction-smoke] %s\n' "$*"; }

resolve_runtime_image() {
  if [[ -n "$RUNTIME_IMAGE" && "${BUILD_RUNTIME_IMAGE_FROM_SOURCE:-0}" != "1" && "$FROM_SOURCE" != "1" ]]; then
    RUNTIME_IMAGE_DETAIL="prebuilt ($RUNTIME_IMAGE)"
    log "using prebuilt runtime image: $RUNTIME_IMAGE"
    return 0
  fi

  command -v docker >/dev/null 2>&1 || fail "docker required to build runtime image from source"
  docker info >/dev/null 2>&1 || fail "docker daemon required to build runtime image from source"

  local image_tag="${RUNTIME_IMAGE:-$LOCAL_SMOKE_TAG}"
  log "building runtime host image from exact-head source → $image_tag"
  docker build \
    -f src/runtime/Sekiban.Dcb.WasmRuntime.Host/Dockerfile \
    -t "$image_tag" \
    "$ROOT" || fail "docker build of runtime host from source failed"
  RUNTIME_IMAGE="$image_tag"
  local image_id digest
  image_id="$(docker image inspect "$RUNTIME_IMAGE" --format '{{.Id}}' 2>/dev/null || true)"
  digest="$(docker image inspect "$RUNTIME_IMAGE" --format '{{index .RepoDigests 0}}' 2>/dev/null || true)"
  if [[ -n "$digest" && "$digest" != "<no value>" ]]; then
    RUNTIME_IMAGE_DETAIL="local-build ($RUNTIME_IMAGE, digest=$digest)"
  elif [[ -n "$image_id" ]]; then
    RUNTIME_IMAGE_DETAIL="local-build ($RUNTIME_IMAGE, id=${image_id#sha256:})"
  else
    RUNTIME_IMAGE_DETAIL="local-build ($RUNTIME_IMAGE)"
  fi
  log "built runtime image: $RUNTIME_IMAGE_DETAIL"
}

write_report() {
  local result="$1" detail="$2"
  mkdir -p "$REPORT_DIR"
  {
    printf '# npm Extraction Smoke\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Packages: \`@sekiban/as-wasm@$AS_VERSION\` (packed tarball) + \`@sekiban/dcb-core/domain/client@0.2.0\` (registry)"
    printf '%s\n' "- Runtime image: \`$RUNTIME_IMAGE\`${RUNTIME_IMAGE_DETAIL:+ ($RUNTIME_IMAGE_DETAIL)}"
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

resolve_runtime_image

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
    dependencies: { '@sekiban/as-wasm': 'file:$AS_TGZ', 'visitor-as': '^0.11.4' },
    devDependencies: { typescript: '^5.7.3', '@types/node': '^22.10.5', assemblyscript: '^0.27.32', 'json-as': '^0.9.28' },
    overrides: { 'visitor-as': { assemblyscript: '\$assemblyscript' } }
  }, null, 2));
"
(cd "$PROJ_DIR" && npm install --no-audit --no-fund >/dev/null 2>&1) || fail "projector consumer npm install failed"
(cd "$PROJ_DIR" && npx asc assembly/index.ts \
  --outFile modules/ts-weather.wasm \
  --optimize --exportStart _initialize --runtime incremental \
  --exportRuntime --use abort= --transform json-as/transform) \
  || fail "asc compile against packed @sekiban/as-wasm tarball failed"
SMOKE_WASM="$PROJ_DIR/modules/ts-weather.wasm"
[[ -s "$SMOKE_WASM" ]] || fail "projector compile produced no wasm module"

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

run_container_check() {
  if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    CONTAINER_RESULT="SKIPPED"
    CONTAINER_DETAIL="Docker is not available in this environment"
    log "SKIPPED container load: $CONTAINER_DETAIL"
    return 0
  fi

  local config_dir="$SMOKE_ROOT/runtime-config"
  mkdir -p "$config_dir"
  sed 's#\./ts-weather\.wasm#/app/modules/ts-weather.wasm#g' \
    "$SAMPLE_ROOT/modules/sekiban-runtime-manifest.json" > "$config_dir/sekiban-manifest.json"

  local port
  port="$(node -e 'const s=require("net").createServer();s.listen(0,()=>{console.log(s.address().port);s.close();})')"

  DOCKER_NETWORK="swr-npm-smoke-$$"
  docker network create "$DOCKER_NETWORK" >/dev/null 2>&1 || true
  log "starting disposable Postgres sidecar"
  PG_CONTAINER_ID="$(docker run -d --rm \
    --network "$DOCKER_NETWORK" --network-alias smoke-postgres \
    -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=sekiban \
    postgres:16-alpine 2>/dev/null)"
  if [[ -z "$PG_CONTAINER_ID" ]]; then
    CONTAINER_RESULT="FAIL"
    CONTAINER_DETAIL="could not start the Postgres sidecar"
    return 1
  fi

  local pg_deadline=$(( $(date +%s) + 60 ))
  local pg_ready=0
  while [[ $(date +%s) -lt $pg_deadline ]]; do
    if docker exec "$PG_CONTAINER_ID" pg_isready -U postgres >/dev/null 2>&1; then pg_ready=1; break; fi
    sleep 2
  done
  if [[ "$pg_ready" != "1" ]]; then
    CONTAINER_RESULT="FAIL"
    CONTAINER_DETAIL="Postgres sidecar did not become ready within 60s"
    return 1
  fi

  log "starting $RUNTIME_IMAGE on port $port"
  CONTAINER_ID="$(docker run -d --rm \
    --network "$DOCKER_NETWORK" \
    -p "$port:8080" \
    -v "$PROJ_DIR/modules:/app/modules:ro" \
    -v "$config_dir:/app/config:ro" \
    -e SEKIBAN_MANIFEST_PATH=/app/config/sekiban-manifest.json \
    -e WASM_MODULE_PATH=/app/modules/ts-weather.wasm \
    -e "ConnectionStrings__SekibanDcb=Host=smoke-postgres;Port=5432;Database=sekiban;Username=postgres;Password=postgres" \
    "$RUNTIME_IMAGE" 2>/dev/null)"
  if [[ -z "$CONTAINER_ID" ]]; then
    CONTAINER_RESULT="FAIL"
    CONTAINER_DETAIL="docker run failed (image pull or start error)"
    return 1
  fi

  local deadline=$(( $(date +%s) + READY_TIMEOUT ))
  local ready=0 code
  while [[ $(date +%s) -lt $deadline ]]; do
    if ! docker ps -q --no-trunc | grep -q "$CONTAINER_ID"; then
      CONTAINER_RESULT="FAIL"
      CONTAINER_DETAIL="container exited before /ready"
      return 1
    fi
    code="$(curl -q -s -o /dev/null --max-time 5 -w '%{http_code}' "http://localhost:$port/ready" || true)"
    if [[ "$code" == "200" ]]; then ready=1; break; fi
    sleep 3
  done
  if [[ "$ready" != "1" ]]; then
    CONTAINER_RESULT="FAIL"
    CONTAINER_DETAIL="/ready did not return 200 within ${READY_TIMEOUT}s"
    return 1
  fi
  log "/ready OK — wasm loaded by the runtime container ($RUNTIME_IMAGE)"

  local evidence
  evidence="$(cd "$CLIENT_SMOKE_DIR" && RUNTIME_URL="http://localhost:$port" node dist/main.js 2>&1)"
  if [[ $? -ne 0 ]]; then
    CONTAINER_RESULT="FAIL"
    CONTAINER_DETAIL="registry DCB client failed against the container: ${evidence:0:400}"
    return 1
  fi
  CONTAINER_RESULT="PASS"
  CONTAINER_DETAIL="registry @sekiban/dcb-* client committed/read/queried through the container: ${evidence:0:400}"
  log "registry client runtime evidence: $evidence"
  return 0
}

run_container_check || fail "container load check failed: $CONTAINER_DETAIL"

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  [[ "$CONTAINER_RESULT" == "PASS" ]] \
    || fail "Docker is available but container runtime proof did not pass ($CONTAINER_RESULT${CONTAINER_DETAIL:+ — $CONTAINER_DETAIL})"
fi

write_report "PASS" "Packed @sekiban/as-wasm, compiled projector wasm, registry @sekiban/dcb-* 0.2.0 client build/test succeeded, and container runtime check ${CONTAINER_RESULT}."
log "PASS (container load: $CONTAINER_RESULT)"
exit 0
