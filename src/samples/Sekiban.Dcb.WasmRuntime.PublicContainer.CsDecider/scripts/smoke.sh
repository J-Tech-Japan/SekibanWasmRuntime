#!/usr/bin/env bash
#
# Public-container sample smoke: starts the Aspire AppHost (Postgres + the PUBLIC
# runtime container with the sample's WASM module + manifest mounted), then proves
# commit + tag-state read + list-query through the running container.
#
# Writes reports/smoke/public-container-cs-decider-smoke.md (PASS / FAIL / SKIP).
# Exit 0 on PASS or SKIP (prereq missing), 1 on FAIL.

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider"
APPHOST="$SAMPLE_DIR/AppHost/PublicContainerCsDecider.AppHost.csproj"
ARTIFACT_DIR="$ROOT/artifacts/samples/public-container-cs-decider"
MODULE="$ARTIFACT_DIR/modules/public-container-cs-decider.wasm"
MANIFEST="$ARTIFACT_DIR/config/sekiban-manifest.json"
REPORT_DIR="$ROOT/reports/smoke"
REPORT="$REPORT_DIR/public-container-cs-decider-smoke.md"
TIMEOUT="${SAMPLE_SMOKE_TIMEOUT:-300}"
APPHOST_PID=""
APPHOST_LOG="$(mktemp)"

log() { printf '[smoke] %s\n' "$*"; }

# Talk to the runtime with a clean curl config: a user-level ~/.curlrc (e.g. a
# hardened allowlist) must not change how the smoke reaches the local runtime.
# `-q` (first arg) makes curl ignore curlrc.
curlq() { curl -q "$@"; }

# Best-effort tail of the runtime container's logs for failure diagnostics. Aspire
# DCP names the container from the "runtime" resource; match the public image.
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
    printf '# Public Container CS Decider Smoke (SWR-G036)\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Runtime image: \`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:${SAMPLE_RUNTIME_IMAGE_TAG:-1.0.0-preview.1}\`"
    printf '%s\n' "- Runtime URL: \`${RUNTIME_URL:-unresolved}\`"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
    if [[ -n "${LAST_HTTP_BODY:-}" ]]; then
      printf '\n## Last HTTP response body\n\n```\n%s\n```\n' "$LAST_HTTP_BODY"
    fi
    if [[ -n "${RUNTIME_LOG_TAIL:-}" ]]; then
      printf '\n## Runtime container log (tail)\n\n```\n%s\n```\n' "$RUNTIME_LOG_TAIL"
    fi
    if [[ -n "${SMOKE_LOG_TAIL:-}" ]]; then
      printf '\n## AppHost log (tail)\n\n```\n%s\n```\n' "$SMOKE_LOG_TAIL"
    fi
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
  if [[ -n "${RUNTIME_LOG_TAIL:-}" ]]; then
    log "--- runtime container log (tail) ---"
    printf '%s\n' "$RUNTIME_LOG_TAIL"
  fi
  write_report "FAIL" "$*"; exit 1
}

command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1 || skip "Docker is not available (the runtime container needs it)."
command -v dotnet >/dev/null 2>&1 || skip "dotnet SDK not found."

# Ensure the WASM module + manifest exist (build if missing).
if [[ ! -s "$MODULE" || ! -s "$MANIFEST" ]]; then
  log "building WASM module + manifest"
  if ! bash "$SAMPLE_DIR/scripts/build-wasm.sh"; then
    skip "could not build the WASM module (see build output); set up the WASI/Docker build and re-run."
  fi
fi

free_port() { python3 -c 'import socket;s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()'; }
RUNTIME_PORT="$(free_port)"
RUNTIME_URL="http://localhost:${RUNTIME_PORT}"
export SAMPLE_RUNTIME_HOST_PORT="$RUNTIME_PORT"
log "runtime host port=$RUNTIME_PORT"

# Launching the AppHost headlessly via `dotnet run` requires the Aspire dashboard
# endpoints to be configured; provide them on free ports for the smoke.
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
log "runtime healthy"

# Schema-aware readiness gate. /ready fails closed when the DCB Postgres schema
# (dcb_events) is absent, so this guards against a runtime that is live but whose
# first commit would fail with `42P01: relation "dcb_events" does not exist`.
log "waiting up to ${TIMEOUT}s for ${RUNTIME_URL}/ready (schema-aware)"
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
[[ "$ready" == "1" ]] || fail "runtime did not become READY within ${TIMEOUT}s (DCB schema may be missing): ${LAST_HTTP_BODY:0:300}"
log "runtime ready (schema present)"

root_resp=$(curlq -s --max-time 10 "$RUNTIME_URL/" || true)
printf '%s' "$root_resp" | grep -q "Sekiban WASM Runtime Host" || fail "service at $RUNTIME_URL is not the Sekiban runtime: ${root_resp:0:200}"
log "runtime identity confirmed"

http_post() { curlq -s --max-time 60 -w '\n%{http_code}' -H 'Content-Type: application/json' -X POST -d "$2" "$RUNTIME_URL$1"; }

forecast_id="sample-$(date +%Y%m%d%H%M%S)-${RANDOM}"
tag="weather:${forecast_id}"
created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
payload_b64=$(printf '{"forecastId":"%s","location":"Kyoto","temperatureC":24,"summary":"Sample","createdAt":"%s"}' "$forecast_id" "$created_at" | base64 | tr -d '\n')
commit_body=$(printf '{"eventCandidates":[{"payload":"%s","eventPayloadName":"WeatherForecastCreated","tags":["%s"]}],"consistencyTags":[]}' "$payload_b64" "$tag")

log "commit WeatherForecastCreated (tag=$tag)"
out=$(http_post "/api/sekiban/serialized/commit" "$commit_body"); code=$(printf '%s' "$out" | tail -n1)
LAST_HTTP_BODY="$(printf '%s' "$out" | sed '$d')"
[[ "$code" == "200" ]] || fail "commit returned HTTP $code: ${LAST_HTTP_BODY}"
log "commit OK"

log "tag-state read (tag-latest-sortable)"
out=$(http_post "/api/sekiban/serialized/tag-latest-sortable" "$(printf '{"tag":"%s"}' "$tag")")
resp=$(printf '%s' "$out" | sed '$d')
printf '%s' "$resp" | python3 -c 'import json,sys; d=json.load(sys.stdin); sys.exit(0 if d.get("exists") else 1)' \
  || fail "tag-state read did not reflect the committed event: $resp"
log "tag-state read OK"

log "list-query (GetWeatherForecastListQuery)"
query_found=0
for i in $(seq 1 20); do
  out=$(http_post "/api/sekiban/serialized/list-query" '{"queryType":"GetWeatherForecastListQuery","queryParamsJson":"{}"}')
  resp=$(printf '%s' "$out" | sed '$d')
  if printf '%s' "$resp" | grep -q "$forecast_id"; then query_found=1; break; fi
  sleep 2
done
[[ "$query_found" == "1" ]] || fail "list-query did not return the committed forecast within timeout: ${resp:0:200}"
log "list-query OK"

# Materialized View path (caller-owned read). The runtime host has no MV read API by
# design; a caller reads MV state directly from DcbMaterializedViewPostgres. After commit,
# the host's MV catch-up worker applies WeatherForecastCreated to the WASM MV projector,
# which writes the row to the physical table named in sekiban_mv_registry. We locate the
# Aspire Postgres container, resolve the MV database + physical table, and poll for the row.
log "materialized-view read (DcbMaterializedViewPostgres)"
pg_cid="$(docker ps --filter ancestor=postgres --format '{{.ID}}' 2>/dev/null | head -1)"
[[ -n "$pg_cid" ]] || pg_cid="$(docker ps --format '{{.ID}} {{.Names}}' 2>/dev/null | awk '/ pg-/{print $1; exit}')"
[[ -n "$pg_cid" ]] || fail "could not locate the Aspire Postgres container to verify the materialized view"

MV_DB="$(docker exec "$pg_cid" psql -U postgres -tAc \
  "SELECT datname FROM pg_database WHERE datname ILIKE 'dcbmaterializedview%' LIMIT 1;" 2>/dev/null | tr -d '[:space:]')"
[[ -n "$MV_DB" ]] || fail "DcbMaterializedViewPostgres database was not created (MV runtime not provisioned)"
psql_mv() { docker exec "$pg_cid" psql -U postgres -d "$MV_DB" -tAc "$1" 2>/dev/null; }

mv_found=0
mv_detail=""
for i in $(seq 1 30); do
  mv_table="$(psql_mv "SELECT physical_table FROM sekiban_mv_registry WHERE view_name='WeatherForecast' AND logical_table='weather_forecast' LIMIT 1;" | tr -d '[:space:]')"
  if [[ -n "$mv_table" ]]; then
    mv_loc="$(psql_mv "SELECT location FROM \"$mv_table\" WHERE forecast_id='$forecast_id' AND is_deleted=FALSE LIMIT 1;" | tr -d '[:space:]')"
    if [[ -n "$mv_loc" ]]; then mv_found=1; mv_detail="table=$mv_table location=$mv_loc"; break; fi
  fi
  sleep 2
done
[[ "$mv_found" == "1" ]] || fail "materialized view did not catch up the committed forecast in DcbMaterializedViewPostgres within timeout (db=$MV_DB)"
log "materialized-view read OK ($mv_detail)"

IMAGE_TAG_USED="${SAMPLE_RUNTIME_IMAGE_TAG:-1.0.0-preview.1}"
log "PASS: commit + tag-state read + list-query + materialized-view read all succeeded through the public runtime container"
write_report "PASS" "Committed WeatherForecastCreated (tag=$tag); read it back via tag-latest-sortable; saw it in GetWeatherForecastListQuery; and confirmed the WeatherForecast materialized view caught it up in DcbMaterializedViewPostgres ($mv_detail) — all through ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:${IMAGE_TAG_USED}."
exit 0
