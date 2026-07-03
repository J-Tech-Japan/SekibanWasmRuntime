#!/usr/bin/env bash
# MoonBit mooncakes external-consumer smoke (SWR-G065).
#
# Two-stage verification:
#   default            Registry-resolved proof: the committed manifests'
#                      sekiban/sekiban-wasm-runtime + sekiban/sekiban-client
#                      registry dependencies resolve from mooncakes.io. This
#                      is the release evidence; it requires the packages to be
#                      published (human-gated account/scope batch).
#   --local-packages   Pre-publish DRY-RUN (NOT release evidence): builds a
#                      STAGED COPY of the sample whose manifests are rewritten
#                      to path dependencies on src/lib/sekiban-moonbit. The
#                      committed manifests are never modified.
#
# The four consumer-proof checks against the public GHCR runtime container:
#   1. command execution (WeatherForecastCreated + WeatherForecastLocationUpdated commits)
#   2. tag-state readback (projected state shows the updated location)
#   3. in-memory projection query (list + count via the sekiban-client package)
#   4. materialized-view catch-up/read (WeatherForecast MV row with the updated location)
#
# Writes reports/smoke/mooncakes-mb-decider-smoke.md (PASS / FAIL / SKIP).
# Exit 0 on PASS or SKIP (prereq missing), 1 on FAIL.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.Mooncakes.MbDecider"
APPHOST="$SAMPLE_DIR/AppHost/MooncakesMbDecider.AppHost.csproj"
ARTIFACT_DIR="$ROOT/artifacts/samples/mooncakes-mb-decider"
STAGED_DIR="$ARTIFACT_DIR/staged"
MODULE="$ARTIFACT_DIR/modules/mooncakes-mb-decider.wasm"
CONFIG="$ARTIFACT_DIR/config/sekiban-manifest.json"
REPORT_DIR="$ROOT/reports/smoke"
REPORT="$REPORT_DIR/mooncakes-mb-decider-smoke.md"
TIMEOUT="${SAMPLE_SMOKE_TIMEOUT:-300}"
APPHOST_PID=""
APPHOST_LOG="$(mktemp)"

MODE="registry-resolved"
if [[ "${1:-}" == "--local-packages" ]]; then
  MODE="local-packages"
  shift
fi

if [[ "$MODE" == "registry-resolved" ]]; then
  MODE_DETAIL="registry-resolved (mooncakes.io sekiban/sekiban-wasm-runtime + sekiban/sekiban-client; requires the published packages)"
  CLIENT_DIR="$ROOT/$SAMPLE_DIR/client"
else
  MODE_DETAIL="LOCAL-PACKAGES DRY-RUN via a staged sample copy with path deps on src/lib/sekiban-moonbit — pre-publish validation only, NOT release evidence"
  CLIENT_DIR="$STAGED_DIR/client"
fi

log() { printf '[mooncakes-mb-smoke] %s\n' "$*"; }
curlq() { curl -q "$@"; }

write_report() {
  local result="$1" detail="$2"
  mkdir -p "$REPORT_DIR"
  {
    printf '# MoonBit Mooncakes External-Consumer Smoke (SWR-G065)\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Mode: $MODE_DETAIL"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Runtime image: \`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:${SAMPLE_RUNTIME_IMAGE_TAG:-1.0.0-preview.3}\`"
    printf '%s\n' "- MoonBit packages: \`sekiban/sekiban-wasm-runtime\` + \`sekiban/sekiban-client\` 0.1.0 (committed manifests are registry-only)"
    printf '%s\n' "- Runtime URL: \`${RUNTIME_URL:-unresolved}\`"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
    [[ -n "${CLIENT_EVIDENCE:-}" ]] && printf '\n## MoonBit client evidence\n\n```json\n%s\n```\n' "$CLIENT_EVIDENCE"
    [[ -n "${MV_DETAIL:-}" ]] && printf '\n## Materialized view\n\n%s\n' "$MV_DETAIL"
    [[ -n "${LAST_HTTP_BODY:-}" ]] && printf '\n## Last HTTP response body\n\n```\n%s\n```\n' "$LAST_HTTP_BODY"
    [[ -n "${SMOKE_LOG_TAIL:-}" ]] && printf '\n## AppHost log (tail)\n\n```\n%s\n```\n' "$SMOKE_LOG_TAIL"
  } > "$REPORT"
  log "report: ${REPORT#$ROOT/}"
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
  log "FAIL: $*"
  write_report "FAIL" "$*"
  exit 1
}

command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1 || skip "Docker is not available."
command -v dotnet >/dev/null 2>&1 || skip "dotnet SDK not found."
command -v moon >/dev/null 2>&1 || skip "moon toolchain not found."

log "mode: $MODE_DETAIL"

log "dependency guard"
bash "$SAMPLE_DIR/scripts/verify-no-local-sekiban-paths.sh" >/dev/null || fail "dependency guard failed"

BUILD_ARGS=()
if [[ "$MODE" == "local-packages" ]]; then
  BUILD_ARGS+=("--local-packages")
fi

log "building MoonBit WASM module + manifest"
if ! bash "$SAMPLE_DIR/scripts/build-wasm.sh" "${BUILD_ARGS[@]}"; then
  if [[ "$MODE" == "registry-resolved" ]]; then
    fail "could not build from mooncakes.io registry dependencies; are the sekiban packages published?"
  fi
  skip "could not build the MoonBit WASM module; install the moon toolchain and re-run."
fi

log "building MoonBit client (native)"
if ! (cd "$CLIENT_DIR" && moon build --target native --release >/dev/null 2>&1); then
  if [[ "$MODE" == "registry-resolved" ]]; then
    fail "could not build the client from mooncakes.io registry dependencies"
  fi
  fail "could not build the staged MoonBit client"
fi
CLIENT_BIN="$CLIENT_DIR/_build/native/release/build/mooncakes-mb-decider-client.exe"
[[ -x "$CLIENT_BIN" ]] || fail "client binary not found at $CLIENT_BIN"

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
export SAMPLE_UPDATED_LOCATION="Osaka"
export SAMPLE_CREATED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

log "running typed MoonBit client (command x2 + tag-state + list/count query via sekiban-client)"
CLIENT_EVIDENCE="$("$CLIENT_BIN" 2>>"$APPHOST_LOG")"
client_status=$?
if [[ "$client_status" != "0" ]] || printf '%s' "$CLIENT_EVIDENCE" | grep -q MB_CLIENT_FAIL; then
  fail "typed MoonBit client smoke failed: ${CLIENT_EVIDENCE:0:500}"
fi
log "typed MoonBit client OK"

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
MV_DETAIL=""
for _ in $(seq 1 30); do
  mv_table="$(psql_mv "SELECT physical_table FROM sekiban_mv_registry WHERE view_name='WeatherForecast' AND logical_table='weather_forecast' LIMIT 1;" | tr -d '[:space:]')"
  if [[ -n "$mv_table" ]]; then
    mv_loc="$(psql_mv "SELECT location FROM \"$mv_table\" WHERE forecast_id='$forecast_id' LIMIT 1;" | tr -d '[:space:]')"
    if [[ "$mv_loc" == "Osaka" ]]; then mv_found=1; MV_DETAIL="table=$mv_table location=$mv_loc (updated location caught up)"; break; fi
  fi
  sleep 2
done
[[ "$mv_found" == "1" ]] || fail "materialized view did not catch up forecast $forecast_id (expected location=Osaka) in $MV_DB"
log "materialized-view OK ($MV_DETAIL)"

write_report "PASS" "Typed MoonBit client (sekiban-client) committed WeatherForecastCreated + WeatherForecastLocationUpdated through the public GHCR runtime running the sekiban-wasm-runtime-built module, read the updated tag state, confirmed list/count queries, and the WeatherForecast MV caught up in DcbMaterializedViewPostgres ($MV_DETAIL)."
log "PASS ($MODE_DETAIL)"
exit 0
