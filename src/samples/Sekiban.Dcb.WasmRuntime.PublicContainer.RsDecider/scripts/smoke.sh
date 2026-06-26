#!/usr/bin/env bash
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider"
APPHOST="$SAMPLE_DIR/AppHost/PublicContainerRsDecider.AppHost.csproj"
MANIFEST="$SAMPLE_DIR/Cargo.toml"
ARTIFACT_DIR="$ROOT/artifacts/samples/public-container-rs-decider"
MODULE="$ARTIFACT_DIR/modules/public-container-rs-decider.wasm"
CONFIG="$ARTIFACT_DIR/config/sekiban-manifest.json"
REPORT_DIR="$ROOT/reports/smoke"
REPORT="$REPORT_DIR/public-container-rs-decider-smoke.md"
TIMEOUT="${SAMPLE_SMOKE_TIMEOUT:-300}"
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
    printf '# Public Container Rust Decider Smoke (SWR-G048)\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Runtime image: \`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:${SAMPLE_RUNTIME_IMAGE_TAG:-1.0.0-preview.3}\`"
    printf '%s\n' "- Runtime URL: \`${RUNTIME_URL:-unresolved}\`"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
    [[ -n "${CLIENT_EVIDENCE:-}" ]] && printf '\n## Rust client evidence\n\n```json\n%s\n```\n' "$CLIENT_EVIDENCE"
    [[ -n "${LAST_HTTP_BODY:-}" ]] && printf '\n## Last HTTP response body\n\n```\n%s\n```\n' "$LAST_HTTP_BODY"
    [[ -n "${RUNTIME_LOG_TAIL:-}" ]] && printf '\n## Runtime container log (tail)\n\n```\n%s\n```\n' "$RUNTIME_LOG_TAIL"
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
  RUNTIME_LOG_TAIL="$(runtime_container_logs 2>/dev/null)"
  log "FAIL: $*"
  write_report "FAIL" "$*"
  exit 1
}

command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1 || skip "Docker is not available."
command -v dotnet >/dev/null 2>&1 || skip "dotnet SDK not found."
command -v cargo >/dev/null 2>&1 || skip "cargo not found."

if [[ ! -s "$MODULE" || ! -s "$CONFIG" ]]; then
  log "building Rust WASM module + manifest"
  if ! bash "$SAMPLE_DIR/scripts/build-wasm.sh"; then
    skip "could not build the Rust WASM module; install the wasm32-wasip1 target and re-run."
  fi
fi

free_port() { python3 -c 'import socket;s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()'; }
RUNTIME_PORT="$(free_port)"
RUNTIME_URL="http://localhost:${RUNTIME_PORT}"
export SAMPLE_RUNTIME_HOST_PORT="$RUNTIME_PORT"
log "runtime host port=$RUNTIME_PORT"

export ASPIRE_ALLOW_UNSECURED_TRANSPORT=true
export ASPNETCORE_URLS="http://localhost:$(free_port)"
export ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:$(free_port)"
export ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL="http://localhost:$(free_port)"

log "starting Aspire AppHost (Postgres + public runtime container)"
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

log "running typed Rust client smoke (RemoteSekibanExecutor + Command + ListQuery)"
CLIENT_EVIDENCE="$(cargo run --manifest-path "$MANIFEST" --package public-container-rs-decider-client --release 2>>"$APPHOST_LOG")"
client_status=$?
[[ "$client_status" == "0" ]] || fail "typed Rust client smoke failed: ${CLIENT_EVIDENCE:0:500}"
log "typed Rust client OK"

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
    mv_loc="$(psql_mv "SELECT location FROM \"$mv_table\" WHERE forecast_id='$forecast_id' AND is_deleted=FALSE LIMIT 1;" | tr -d '[:space:]')"
    if [[ -n "$mv_loc" ]]; then mv_found=1; mv_detail="table=$mv_table location=$mv_loc"; break; fi
  fi
  sleep 2
done
[[ "$mv_found" == "1" ]] || fail "materialized view did not catch up forecast $forecast_id in $MV_DB"
log "materialized-view OK ($mv_detail)"

write_report "PASS" "Typed Rust client committed forecast $forecast_id through RemoteSekibanExecutor, read tag-state and GetWeatherForecastListQuery, and confirmed WeatherForecast MV catch-up in DcbMaterializedViewPostgres ($mv_detail)."
log "PASS"
exit 0
