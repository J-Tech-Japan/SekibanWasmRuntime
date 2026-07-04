#!/usr/bin/env bash
# End-to-end smoke for the npm TypeScript external-consumer sample against the
# public GHCR runtime container. SEKIBAN_NPM_MODE (registry|tarball) selects
# how @sekiban/as-wasm and @sekiban/ts are resolved; see build-wasm.sh and
# the README for details. Skips gracefully (exit 0, "Result: SKIP") when
# Docker, the .NET SDK, npm, or node are unavailable, or when registry mode
# cannot resolve the not-yet-published packages.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider"
APPHOST="$SAMPLE_DIR/AppHost/NpmTsDecider.AppHost.csproj"
ARTIFACT_DIR="$ROOT/artifacts/samples/npm-ts-decider"
MODULE="$ARTIFACT_DIR/modules/npm-ts-decider.wasm"
CONFIG="$ARTIFACT_DIR/config/sekiban-manifest.json"
REPORT_DIR="$ROOT/reports/smoke"
REPORT="$REPORT_DIR/npm-ts-decider-smoke.md"
TIMEOUT="${SAMPLE_SMOKE_TIMEOUT:-300}"
NPM_MODE="${SEKIBAN_NPM_MODE:-registry}"
APPHOST_PID=""
APPHOST_LOG="$(mktemp)"

log() { printf '[smoke] %s\n' "$*"; }
curlq() { curl -q "$@"; }

runtime_container_logs() {
  local cid
  cid="$(docker ps -a --format '{{.ID}} {{.Image}}' 2>/dev/null \
    | awk '/sekiban-wasm-runtime-host/{print $1; exit}')"
  [[ -n "$cid" ]] && docker logs --tail 120 "$cid" 2>&1
}

write_report() {
  local result="$1" detail="$2"
  mkdir -p "$REPORT_DIR"
  {
    printf '# npm TypeScript Decider Smoke (SWR-G059)\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Runtime image: \`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:${SAMPLE_RUNTIME_IMAGE_TAG:-1.0.0-preview.3}\`"
    printf '%s\n' "- Sekiban packages: npm \`@sekiban/ts@0.1.0\`, \`@sekiban/as-wasm@0.1.0\` (SEKIBAN_NPM_MODE=$NPM_MODE, no local Sekiban path dependencies)"
    printf '%s\n' "- Runtime URL: \`${RUNTIME_URL:-unresolved}\`"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
    [[ -n "${CLIENT_EVIDENCE:-}" ]] && printf '\n## TypeScript client evidence\n\n```json\n%s\n```\n' "$CLIENT_EVIDENCE"
    [[ -n "${LAST_HTTP_BODY:-}" ]] && printf '\n## Last HTTP response body\n\n```\n%s\n```\n' "$LAST_HTTP_BODY"
    [[ -n "${RUNTIME_LOG_TAIL:-}" ]] && printf '\n## Runtime container log (tail)\n\n```\n%s\n```\n' "$RUNTIME_LOG_TAIL"
    [[ -n "${SMOKE_LOG_TAIL:-}" ]] && printf '\n## AppHost log (tail)\n\n```\n%s\n```\n' "$SMOKE_LOG_TAIL"
  } > "$REPORT"
  log "report: ${REPORT#"$ROOT"/}"
}

cleanup() {
  if [[ -n "$APPHOST_PID" ]] && kill -0 "$APPHOST_PID" 2>/dev/null; then
    log "stopping AppHost ($APPHOST_PID)"
    kill "$APPHOST_PID" 2>/dev/null || true
    wait "$APPHOST_PID" 2>/dev/null || true
  fi
  rm -f "$APPHOST_LOG"
}
trap cleanup EXIT

skip() { log "SKIP: $*"; write_report "SKIP" "$*"; exit 0; }
fail() {
  SMOKE_LOG_TAIL="$(tail -120 "$APPHOST_LOG" 2>/dev/null)"
  RUNTIME_LOG_TAIL="$(runtime_container_logs 2>/dev/null)"
  log "FAIL: $*"
  write_report "FAIL" "$*"
  exit 1
}

command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1 || skip "Docker is not available."
command -v dotnet >/dev/null 2>&1 || skip "dotnet SDK not found."
command -v npm >/dev/null 2>&1 || skip "npm not found."
command -v node >/dev/null 2>&1 || skip "node not found."
command -v curl >/dev/null 2>&1 || skip "curl not found."
command -v python3 >/dev/null 2>&1 || skip "python3 not found."

if [[ ! -s "$MODULE" || ! -s "$CONFIG" ]]; then
  log "building AssemblyScript WASM module + manifest (SEKIBAN_NPM_MODE=$NPM_MODE)"
  if ! SEKIBAN_NPM_MODE="$NPM_MODE" bash "$SAMPLE_DIR/scripts/build-wasm.sh"; then
    if [[ "$NPM_MODE" == "registry" ]]; then
      skip "could not build the WASM module in registry mode -- @sekiban/as-wasm is not published yet; re-run with SEKIBAN_NPM_MODE=tarball."
    fi
    skip "could not build the WASM module."
  fi
fi

log "preparing TypeScript client (SEKIBAN_NPM_MODE=$NPM_MODE)"
CLIENT_DIR="$ROOT/$SAMPLE_DIR/Client"
CLIENT_BUILD_DIR="$CLIENT_DIR"
case "$NPM_MODE" in
  registry)
    if ! (cd "$CLIENT_DIR" && npm install --no-audit --no-fund >/dev/null 2>&1); then
      skip "npm registry install failed for the TypeScript client -- @sekiban/ts is not published yet; re-run with SEKIBAN_NPM_MODE=tarball."
    fi
    ;;
  tarball)
    TARBALL_DIR="$ARTIFACT_DIR/tarballs"
    mkdir -p "$TARBALL_DIR"
    TS_PKG_DIR="$ROOT/src/lib/sekiban-ts"
    npm --prefix "$TS_PKG_DIR" install --no-audit --no-fund >/dev/null 2>&1 || fail "npm install failed for @sekiban/ts"
    TS_TGZ_NAME="$(cd "$TS_PKG_DIR" && npm pack --pack-destination "$TARBALL_DIR" --silent 2>/dev/null | tail -n1)"
    TS_TGZ="$TARBALL_DIR/$TS_TGZ_NAME"
    [[ -s "$TS_TGZ" ]] || fail "npm pack produced no tarball for @sekiban/ts"

    CLIENT_BUILD_DIR="$ARTIFACT_DIR/client-build"
    rm -rf "$CLIENT_BUILD_DIR"
    mkdir -p "$CLIENT_BUILD_DIR"
    cp -R "$CLIENT_DIR/src" "$CLIENT_BUILD_DIR/src"
    cp "$CLIENT_DIR/tsconfig.json" "$CLIENT_BUILD_DIR/tsconfig.json"
    node -e "
      const fs = require('fs');
      const pkg = JSON.parse(fs.readFileSync('$CLIENT_DIR/package.json', 'utf8'));
      pkg.dependencies['@sekiban/ts'] = 'file:$TS_TGZ';
      fs.writeFileSync('$CLIENT_BUILD_DIR/package.json', JSON.stringify(pkg, null, 2));
    "
    (cd "$CLIENT_BUILD_DIR" && npm install --no-audit --no-fund >/dev/null 2>&1) \
      || fail "npm install failed for the packed @sekiban/ts tarball"

    resolved="$(node -p "require('$CLIENT_BUILD_DIR/package-lock.json').packages['node_modules/@sekiban/ts'].resolved || ''")"
    case "$resolved" in
      *sekiban-ts-*.tgz) ;;
      *) fail "no-local-path guard: @sekiban/ts resolved to '$resolved' instead of the packed tarball" ;;
    esac
    ;;
  *)
    fail "unknown SEKIBAN_NPM_MODE=$NPM_MODE (expected 'registry' or 'tarball')"
    ;;
esac

log "compiling TypeScript client"
(cd "$CLIENT_BUILD_DIR" && npx tsc) || fail "tsc compile failed"

free_port() { python3 -c 'import socket;s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()'; }
RUNTIME_PORT="$(free_port)"
RUNTIME_URL="http://localhost:${RUNTIME_PORT}"
export SAMPLE_RUNTIME_HOST_PORT="$RUNTIME_PORT"
log "runtime host port=$RUNTIME_PORT"

export ASPIRE_ALLOW_UNSECURED_TRANSPORT=true
export ASPNETCORE_URLS="http://localhost:$(free_port)"
export ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:$(free_port)"
export ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL="http://localhost:$(free_port)"

log "starting Aspire AppHost (Postgres + public GHCR runtime container)"
dotnet run --project "$APPHOST" -c Release > "$APPHOST_LOG" 2>&1 &
APPHOST_PID=$!

log "waiting up to ${TIMEOUT}s for ${RUNTIME_URL}/health"
healthy=0
deadline=$(( $(date +%s) + TIMEOUT ))
while [[ $(date +%s) -lt $deadline ]]; do
  if ! kill -0 "$APPHOST_PID" 2>/dev/null; then fail "AppHost exited before the runtime became healthy"; fi
  code=$(curlq -s -o /dev/null --max-time 5 -w '%{http_code}' "$RUNTIME_URL/health" || true)
  if [[ "$code" == "200" ]]; then healthy=1; break; fi
  sleep 5
done
[[ "$healthy" == "1" ]] || fail "runtime did not become healthy within ${TIMEOUT}s"

log "waiting up to ${TIMEOUT}s for ${RUNTIME_URL}/ready"
ready=0
deadline=$(( $(date +%s) + TIMEOUT ))
while [[ $(date +%s) -lt $deadline ]]; do
  if ! kill -0 "$APPHOST_PID" 2>/dev/null; then fail "AppHost exited before the runtime became ready"; fi
  ready_out=$(curlq -s --max-time 5 -w '\n%{http_code}' "$RUNTIME_URL/ready" || true)
  rcode=$(printf '%s' "$ready_out" | tail -n1)
  LAST_HTTP_BODY=$(printf '%s' "$ready_out" | sed '$d')
  if [[ "$rcode" == "200" ]]; then ready=1; break; fi
  sleep 5
done
[[ "$ready" == "1" ]] || fail "runtime did not become ready within ${TIMEOUT}s: ${LAST_HTTP_BODY:0:300}"

forecast_id="$(python3 - <<'PY'
import uuid
print(uuid.uuid4())
PY
)"
export RUNTIME_URL
export SAMPLE_FORECAST_ID="$forecast_id"
export SAMPLE_FORECAST_LOCATION="Kyoto"

log "running typed TypeScript client smoke (SekibanRuntimeClient + Command + tag-state + in-memory ListQuery/CountQuery)"
CLIENT_EVIDENCE="$(cd "$CLIENT_BUILD_DIR" && node dist/main.js 2>>"$APPHOST_LOG")"
client_status=$?
[[ "$client_status" == "0" ]] || fail "typed TypeScript client smoke failed: ${CLIENT_EVIDENCE:0:500}"
log "typed TypeScript client OK"

log "materialized-view read (DcbMaterializedViewPostgres)"
pg_password_for() { docker exec "$1" printenv POSTGRES_PASSWORD 2>/dev/null | tr -d '\r\n'; }
pg_mv_db_name() {
  local cid="$1" pw; pw="$(pg_password_for "$cid")"
  docker exec -e PGPASSWORD="$pw" "$cid" psql -U postgres -tAc \
    "SELECT datname FROM pg_database WHERE datname ILIKE 'dcbmaterializedview%' LIMIT 1;" 2>/dev/null \
    | tr -d '[:space:]'
}

pg_cid=""; MV_DB=""; PG_PW=""
mv_db_deadline=$(( $(date +%s) + 60 ))
while [[ $(date +%s) -lt $mv_db_deadline ]]; do
  candidates="$( { docker ps --format '{{.Names}} {{.ID}}' 2>/dev/null | awk '$1 ~ /^pg-/ {print $2}'; \
                   docker ps --filter ancestor=postgres --format '{{.ID}}' 2>/dev/null; } \
                 | awk 'NF && !seen[$0]++' )"
  for cid in $candidates; do
    db="$(pg_mv_db_name "$cid")"
    if [[ -n "$db" ]]; then pg_cid="$cid"; MV_DB="$db"; PG_PW="$(pg_password_for "$cid")"; break; fi
  done
  [[ -n "$pg_cid" ]] && break
  sleep 2
done
[[ -n "$pg_cid" ]] || fail "could not locate DcbMaterializedViewPostgres"
psql_mv() { docker exec -e PGPASSWORD="$PG_PW" "$pg_cid" psql -U postgres -d "$MV_DB" -tAc "$1" 2>/dev/null; }

mv_found=0
mv_detail=""
for _ in $(seq 1 30); do
  mv_table="$(psql_mv "SELECT physical_table FROM sekiban_mv_registry WHERE view_name='WeatherForecast' AND logical_table='weather_forecast' LIMIT 1;" | tr -d '[:space:]')"
  if [[ -n "$mv_table" ]]; then
    mv_loc="$(psql_mv "SELECT location FROM \"$mv_table\" WHERE forecast_id='$forecast_id' LIMIT 1;" | tr -d '[:space:]')"
    if [[ -n "$mv_loc" ]]; then mv_found=1; mv_detail="table=$mv_table location=$mv_loc"; break; fi
  fi
  sleep 2
done
[[ "$mv_found" == "1" ]] || fail "materialized view did not catch up forecast $forecast_id in $MV_DB"
log "materialized-view OK ($mv_detail)"

write_report "PASS" "Typed TypeScript client (@sekiban/ts + @sekiban/as-wasm 0.1.0, SEKIBAN_NPM_MODE=$NPM_MODE) committed forecast $forecast_id through SekibanRuntimeClient against the public GHCR runtime, read tag-state and in-memory GetWeatherForecastListQuery/GetWeatherForecastCountQuery, and confirmed WeatherForecast MV catch-up in DcbMaterializedViewPostgres ($mv_detail)."
log "PASS"
exit 0
