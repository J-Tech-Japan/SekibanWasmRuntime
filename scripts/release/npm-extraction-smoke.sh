#!/usr/bin/env bash
# SWR-G057 npm extraction smoke.
#
# Proves the @sekiban/ts and @sekiban/as-wasm package boundaries using only
# packed npm artifacts (no registry credentials, no publish):
#   1. npm pack both packages and validate the tarball contents.
#   2. Compile the ts-wasm sample projector against the packed
#      @sekiban/as-wasm tarball (never the workspace sources).
#   3. Compile the ts-clientapi sample against the packed @sekiban/ts tarball.
#   4. Load the produced .wasm in the public GHCR runtime container
#      (with a disposable Postgres sidecar for the event store, mirroring
#      the docker-compose sample) and drive a tag-state read plus a list
#      query through the packed @sekiban/ts client.
#
# If Docker is unavailable the container-load step is reported as SKIPPED
# explicitly; every other step must pass.
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"
ROOT="$(pwd)"

SMOKE_ROOT="${NPM_EXTRACTION_SMOKE_DIR:-$ROOT/artifacts/npm-extraction-smoke}"
REPORT_DIR="${RELEASE_REPORT_DIR:-$ROOT/artifacts/release}"
REPORT="$REPORT_DIR/npm-extraction-smoke.md"
RUNTIME_IMAGE="ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:${SAMPLE_RUNTIME_IMAGE_TAG:-1.0.0-preview.3}"
READY_TIMEOUT="${NPM_EXTRACTION_SMOKE_TIMEOUT:-180}"

TS_PKG_DIR="$ROOT/src/lib/sekiban-ts"
AS_PKG_DIR="$ROOT/src/lib/sekiban-as-wasm"
SAMPLE_ROOT="$ROOT/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts"
TS_WASM_DIR="$SAMPLE_ROOT/ts-wasm"
CLIENT_DIR="$SAMPLE_ROOT/ts-clientapi"

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
    printf '# npm Extraction Smoke (SWR-G057)\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Packages: \`@sekiban/ts@$TS_VERSION\`, \`@sekiban/as-wasm@$AS_VERSION\` (packed tarballs, nothing published)"
    printf '%s\n' "- Runtime image: \`$RUNTIME_IMAGE\`"
    printf '%s\n' "- Container load: $CONTAINER_RESULT${CONTAINER_DETAIL:+ — $CONTAINER_DETAIL}"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
  } > "$REPORT"
  log "report: ${REPORT#"$ROOT"/}"
}

cleanup() {
  if [[ -n "$CONTAINER_ID" ]]; then
    docker rm -f "$CONTAINER_ID" >/dev/null 2>&1 || true
  fi
  if [[ -n "$PG_CONTAINER_ID" ]]; then
    docker rm -f "$PG_CONTAINER_ID" >/dev/null 2>&1 || true
  fi
  if [[ -n "$DOCKER_NETWORK" ]]; then
    docker network rm "$DOCKER_NETWORK" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

fail() {
  log "FAIL: $*"
  write_report "FAIL" "$*"
  exit 1
}

command -v npm >/dev/null 2>&1 || fail "npm not found"
command -v node >/dev/null 2>&1 || fail "node not found"

TS_VERSION="$(node -p "require('$TS_PKG_DIR/package.json').version")"
AS_VERSION="$(node -p "require('$AS_PKG_DIR/package.json').version")"

rm -rf "$SMOKE_ROOT"
mkdir -p "$SMOKE_ROOT" "$REPORT_DIR"

# ---------------------------------------------------------------------------
# 1. Pack both packages and validate tarball contents
# ---------------------------------------------------------------------------

log "packing @sekiban/ts@$TS_VERSION"
npm --prefix "$TS_PKG_DIR" install --no-audit --no-fund >/dev/null 2>&1 || fail "npm install failed for @sekiban/ts"
TS_TGZ_NAME="$(cd "$TS_PKG_DIR" && npm pack --pack-destination "$SMOKE_ROOT" --silent 2>/dev/null | tail -n1)"
TS_TGZ="$SMOKE_ROOT/$TS_TGZ_NAME"
[[ -s "$TS_TGZ" ]] || fail "npm pack produced no tarball for @sekiban/ts"

log "validating @sekiban/ts tarball contents"
unexpected="$(tar -tzf "$TS_TGZ" | grep -Ev '^package/(dist/|README\.md$|LICENSE$|package\.json$)' || true)"
[[ -z "$unexpected" ]] || fail "@sekiban/ts tarball contains unexpected entries: $unexpected"
tar -tzf "$TS_TGZ" | grep -q '^package/dist/index\.js$' || fail "@sekiban/ts tarball is missing dist/index.js"

log "packing @sekiban/as-wasm@$AS_VERSION"
npm --prefix "$AS_PKG_DIR" install --no-audit --no-fund >/dev/null 2>&1 || fail "npm install failed for @sekiban/as-wasm"
AS_TGZ_NAME="$(cd "$AS_PKG_DIR" && npm pack --pack-destination "$SMOKE_ROOT" --silent 2>/dev/null | tail -n1)"
AS_TGZ="$SMOKE_ROOT/$AS_TGZ_NAME"
[[ -s "$AS_TGZ" ]] || fail "npm pack produced no tarball for @sekiban/as-wasm"

log "validating @sekiban/as-wasm tarball contents"
unexpected="$(tar -tzf "$AS_TGZ" | grep -Ev '^package/(assembly/|README\.md$|LICENSE$|package\.json$)' || true)"
[[ -z "$unexpected" ]] || fail "@sekiban/as-wasm tarball contains unexpected entries: $unexpected"
tar -tzf "$AS_TGZ" | grep -q '^package/assembly/index\.ts$' || fail "@sekiban/as-wasm tarball is missing assembly/index.ts"

# ---------------------------------------------------------------------------
# 2. Compile the sample projector against the packed @sekiban/as-wasm tarball
# ---------------------------------------------------------------------------

PROJ_DIR="$SMOKE_ROOT/projector-consumer"
log "compiling ts-wasm projector sources against $AS_TGZ_NAME"
mkdir -p "$PROJ_DIR/modules"
cp -R "$TS_WASM_DIR/assembly" "$PROJ_DIR/assembly"
cp "$TS_WASM_DIR/tsconfig.json" "$PROJ_DIR/tsconfig.json"

cat > "$PROJ_DIR/package.json" <<EOF
{
  "name": "npm-extraction-smoke-projector",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "@sekiban/as-wasm": "file:$AS_TGZ",
    "visitor-as": "^0.11.4"
  },
  "devDependencies": {
    "assemblyscript": "^0.27.32",
    "json-as": "^0.9.28"
  },
  "overrides": {
    "visitor-as": {
      "assemblyscript": "\$assemblyscript"
    }
  }
}
EOF

npm --prefix "$PROJ_DIR" install --no-audit --no-fund >/dev/null 2>&1 || fail "npm install failed for the projector consumer"

# Guard: @sekiban/as-wasm must resolve to the packed tarball, never to the
# workspace sources under src/lib.
resolved="$(node -p "require('$PROJ_DIR/package-lock.json').packages['node_modules/@sekiban/as-wasm'].resolved || ''")"
case "$resolved" in
  *sekiban-as-wasm-*.tgz) ;;
  *) fail "no-local-path guard: @sekiban/as-wasm resolved to '$resolved' instead of the packed tarball" ;;
esac
if grep -R "file:../../../lib" "$PROJ_DIR/package.json" >/dev/null 2>&1; then
  fail "no-local-path guard: projector consumer references workspace sources"
fi

(cd "$PROJ_DIR" && npx asc assembly/index.ts \
  --outFile modules/ts-weather.wasm \
  --optimize --exportStart _initialize --runtime incremental \
  --exportRuntime --use abort= --transform json-as/transform) \
  || fail "asc compile against the packed @sekiban/as-wasm tarball failed"
SMOKE_WASM="$PROJ_DIR/modules/ts-weather.wasm"
[[ -s "$SMOKE_WASM" ]] || fail "projector compile produced no wasm module"
log "projector wasm built from packed tarball: ${SMOKE_WASM#"$ROOT"/}"

node -e "
const fs = require('fs');
WebAssembly.compile(fs.readFileSync('$SMOKE_WASM')).then(m => {
  const names = new Set(WebAssembly.Module.exports(m).map(e => e.name));
  const required = ['alloc','dealloc','create_instance','apply_event','serialize_state','restore_state','execute_query','execute_list_query','get_event_types','mv_metadata','mv_initialize','mv_apply_event'];
  const missing = required.filter(n => !names.has(n));
  if (missing.length) { console.error('missing exports: ' + missing.join(',')); process.exit(1); }
});" || fail "packed-tarball wasm module is missing required exports"

# ---------------------------------------------------------------------------
# 3. Compile the TS client sample against the packed @sekiban/ts tarball
# ---------------------------------------------------------------------------

CLIENT_SMOKE_DIR="$SMOKE_ROOT/client-consumer"
log "compiling ts-clientapi sources against $TS_TGZ_NAME"
mkdir -p "$CLIENT_SMOKE_DIR"
cp -R "$CLIENT_DIR/src" "$CLIENT_SMOKE_DIR/src"
cp "$CLIENT_DIR/tsconfig.json" "$CLIENT_SMOKE_DIR/tsconfig.json"

cat > "$CLIENT_SMOKE_DIR/package.json" <<EOF
{
  "name": "npm-extraction-smoke-client",
  "version": "0.0.0",
  "private": true,
  "type": "module",
  "dependencies": {
    "@hono/node-server": "^1.13.0",
    "@sekiban/ts": "file:$TS_TGZ",
    "hono": "^4.6.10",
    "pg": "^8.13.1",
    "uuid": "^10.0.0"
  },
  "devDependencies": {
    "typescript": "^5.7.3",
    "@types/node": "^22.10.5",
    "@types/pg": "^8.11.10",
    "@types/uuid": "^10.0.0"
  }
}
EOF

npm --prefix "$CLIENT_SMOKE_DIR" install --no-audit --no-fund >/dev/null 2>&1 || fail "npm install failed for the client consumer"

resolved="$(node -p "require('$CLIENT_SMOKE_DIR/package-lock.json').packages['node_modules/@sekiban/ts'].resolved || ''")"
case "$resolved" in
  *sekiban-ts-*.tgz) ;;
  *) fail "no-local-path guard: @sekiban/ts resolved to '$resolved' instead of the packed tarball" ;;
esac

(cd "$CLIENT_SMOKE_DIR" && npx tsc) || fail "tsc compile against the packed @sekiban/ts tarball failed"
log "client compiled from packed tarball"

# ---------------------------------------------------------------------------
# 4. Load the produced wasm in the public runtime container
# ---------------------------------------------------------------------------

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

  # The published preview.3 image requires a relational event store it can
  # migrate at startup; run a disposable Postgres sidecar like the compose
  # sample does (the sqlite provider crashes at startup in preview.3).
  DOCKER_NETWORK="swr-npm-smoke-$$"
  docker network create "$DOCKER_NETWORK" >/dev/null 2>&1 || true
  log "starting disposable Postgres sidecar"
  PG_CONTAINER_ID="$(docker run -d --rm \
    --network "$DOCKER_NETWORK" --network-alias smoke-postgres \
    -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=sekiban \
    postgres:16-alpine 2>/dev/null)"
  if [[ -z "$PG_CONTAINER_ID" ]]; then
    CONTAINER_RESULT="FAIL"
    CONTAINER_DETAIL="could not start the Postgres sidecar (image pull or start error)"
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
      CONTAINER_DETAIL="container exited before /ready; last logs: $(docker logs --tail 20 "$CONTAINER_ID" 2>&1 | tail -c 400)"
      return 1
    fi
    code="$(curl -q -s -o /dev/null --max-time 5 -w '%{http_code}' "http://localhost:$port/ready" || true)"
    if [[ "$code" == "200" ]]; then ready=1; break; fi
    sleep 3
  done
  if [[ "$ready" != "1" ]]; then
    CONTAINER_RESULT="FAIL"
    CONTAINER_DETAIL="/ready did not return 200 within ${READY_TIMEOUT}s (module load check failed)"
    return 1
  fi
  log "/ready OK — packed-tarball wasm loaded by the public runtime container"

  # Minimal runtime check through the packed @sekiban/ts client: commit a
  # WeatherForecastCreated event, read the tag state back, and run the
  # in-memory list query against the loaded module.
  local evidence
  evidence="$(cd "$CLIENT_SMOKE_DIR" && node --input-type=module -e "
import { SekibanRuntimeClient, newCommandOutput, tagString } from '@sekiban/ts';
import { randomUUID } from 'node:crypto';

const client = new SekibanRuntimeClient('http://localhost:$port', {
  WeatherForecast: 'WeatherForecastProjector',
});
const forecastId = randomUUID();
const tag = tagString('WeatherForecast', forecastId);

const command = {
  commandType: () => 'CreateWeatherForecast',
  handle: async (ctx) => {
    const resp = await ctx.getTagState('WeatherForecast', forecastId);
    return newCommandOutput(
      'WeatherForecastCreated',
      {
        forecastId,
        location: 'Kyoto',
        date: '2026-07-02',
        temperatureC: 21,
        summary: 'npm extraction smoke',
        createdAt: new Date().toISOString(),
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  },
};

await client.finalizeCommand(command);

let state = null;
for (let i = 0; i < 15; i++) {
  state = await client.getTagState('WeatherForecast', forecastId);
  if (state.version > 0) break;
  await new Promise(r => setTimeout(r, 2000));
}
if (!state || state.version < 1) {
  console.error('tag state did not reflect the committed event: ' + JSON.stringify(state));
  process.exit(1);
}

let items = '[]';
for (let i = 0; i < 15; i++) {
  items = await client.executeListQuery('GetWeatherForecastListQuery', JSON.stringify({ forecastId, pageSize: 5, pageNumber: 1 }));
  if (items.includes(forecastId)) break;
  await new Promise(r => setTimeout(r, 2000));
}
if (!items.includes(forecastId)) {
  console.error('list query did not return the committed forecast: ' + items);
  process.exit(1);
}
console.log(JSON.stringify({ forecastId, tagStateVersion: state.version, tagStateJson: JSON.parse(state.stateJson), listQueryItems: JSON.parse(items) }));
" 2>&1)"
  if [[ $? -ne 0 ]]; then
    CONTAINER_RESULT="FAIL"
    CONTAINER_DETAIL="packed @sekiban/ts client failed against the container: ${evidence:0:400}"
    return 1
  fi
  CONTAINER_RESULT="PASS"
  CONTAINER_DETAIL="/ready 200; packed @sekiban/ts client committed WeatherForecastCreated, read tag state, and queried it back: $evidence"
  log "packed @sekiban/ts client evidence: $evidence"
  return 0
}

run_container_check || fail "container load check failed: $CONTAINER_DETAIL"

write_report "PASS" "Packed both tarballs, validated their contents, compiled the projector and client samples against the packed artifacts only, and ran the container load check (${CONTAINER_RESULT})."
log "PASS (container load: $CONTAINER_RESULT)"
exit 0
