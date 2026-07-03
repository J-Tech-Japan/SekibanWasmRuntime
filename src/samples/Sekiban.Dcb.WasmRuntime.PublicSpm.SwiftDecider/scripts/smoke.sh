#!/usr/bin/env bash
# Swift SPM external-consumer smoke (SWR-G063).
#
# Two-stage verification:
#   default            Mirror-resolved proof: the committed Package.swift
#                      dependency on https://github.com/J-Tech-Japan/sekiban-swift
#                      (exact 0.1.0) resolves from the real public mirror.
#                      This is the release evidence; it requires the mirror
#                      repository to be public with tag v0.1.0.
#   --local-package    Pre-publish DRY-RUN (NOT release evidence): stages the
#                      mirror tree with the SWR-G062 sync dry-run, turns it into
#                      a local git repo tagged v0.1.0, and redirects the mirror
#                      URL to it via SwiftPM dependency mirroring
#                      (.swiftpm/configuration/mirrors.json — the committed
#                      Package.swift is never modified).
#
# The four consumer-proof checks against the public GHCR runtime container:
#   1. command execution (WeatherForecastCreated + WeatherForecastLocationUpdated commits)
#   2. tag-state readback (tag-latest-sortable reflects the committed tag)
#   3. in-memory projection query (GetWeatherForecastListQuery shows the updated location)
#   4. materialized-view catch-up/read (WeatherForecast MV row with the updated location)
#
# Writes reports/smoke/public-spm-swift-decider-smoke.md (PASS / FAIL / SKIP).
# Exit 0 on PASS or SKIP (prereq missing), 1 on FAIL.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider"
APPHOST="$SAMPLE_DIR/AppHost/PublicSpmSwiftDecider.AppHost.csproj"
ARTIFACT_DIR="$ROOT/artifacts/samples/public-spm-swift-decider"
MODULE="$ARTIFACT_DIR/modules/public-spm-swift-decider.wasm"
CONFIG="$ARTIFACT_DIR/config/sekiban-manifest.json"
REPORT_DIR="$ROOT/reports/smoke"
REPORT="$REPORT_DIR/public-spm-swift-decider-smoke.md"
TIMEOUT="${SAMPLE_SMOKE_TIMEOUT:-300}"
MIRROR_URL="https://github.com/J-Tech-Japan/sekiban-swift"
LOCAL_MIRROR_ROOT="$ROOT/artifacts/sekiban-swift-mirror/local-package-repo"
APPHOST_PID=""
APPHOST_LOG="$(mktemp)"

MODE="mirror-resolved"
if [[ "${1:-}" == "--local-package" ]]; then
  MODE="local-package"
  shift
fi

if [[ "$MODE" == "mirror-resolved" ]]; then
  MODE_DETAIL="mirror-resolved (public $MIRROR_URL at exact 0.1.0; requires the mirror to be public)"
else
  MODE_DETAIL="LOCAL-PACKAGE DRY-RUN via SwiftPM mirror redirection to the staged tree — pre-publish validation only, NOT release evidence"
fi

log() { printf '[swift-spm-smoke] %s\n' "$*"; }
curlq() { curl -q "$@"; }

write_report() {
  local result="$1" detail="$2"
  mkdir -p "$REPORT_DIR"
  {
    printf '# Swift SPM External-Consumer Smoke (SWR-G063)\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Mode: $MODE_DETAIL"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Runtime image: \`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:${SAMPLE_RUNTIME_IMAGE_TAG:-1.0.0-preview.3}\`"
    printf '%s\n' "- Swift package: \`$MIRROR_URL\` exact 0.1.0 (committed Package.swift is path-free)"
    printf '%s\n' "- Runtime URL: \`${RUNTIME_URL:-unresolved}\`"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
    [[ -n "${MV_DETAIL:-}" ]] && printf '\n## Materialized view\n\n%s\n' "$MV_DETAIL"
    [[ -n "${LAST_HTTP_BODY:-}" ]] && printf '\n## Last HTTP response body\n\n```\n%s\n```\n' "$LAST_HTTP_BODY"
    [[ -n "${SMOKE_LOG_TAIL:-}" ]] && printf '\n## AppHost log (tail)\n\n```\n%s\n```\n' "$SMOKE_LOG_TAIL"
  } > "$REPORT"
  log "report: ${REPORT#$ROOT/}"
}

unset_mirror() {
  (cd "$SAMPLE_DIR" && swift package config unset-mirror --original "$MIRROR_URL" >/dev/null 2>&1) || true
}

cleanup() {
  if [[ -n "$APPHOST_PID" ]] && kill -0 "$APPHOST_PID" 2>/dev/null; then
    log "stopping AppHost ($APPHOST_PID)"
    kill "$APPHOST_PID" 2>/dev/null || true
    wait "$APPHOST_PID" 2>/dev/null || true
  fi
  if [[ "$MODE" == "local-package" ]]; then
    unset_mirror
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
command -v swift >/dev/null 2>&1 || skip "swift toolchain not found."

log "mode: $MODE_DETAIL"

log "dependency guard"
bash "$SAMPLE_DIR/scripts/verify-no-local-sekiban-paths.sh" >/dev/null || fail "dependency guard failed"

if [[ "$MODE" == "local-package" ]]; then
  log "staging local mirror tree (SWR-G062 sync dry-run) and tagging v0.1.0"
  bash scripts/release/sync-sekiban-swift-mirror.sh --dry-run >/dev/null 2>&1 \
    || fail "mirror sync dry-run failed (needed to stage the local package)"
  rm -rf "$LOCAL_MIRROR_ROOT"
  mkdir -p "$LOCAL_MIRROR_ROOT"
  cp -R "$ROOT/artifacts/sekiban-swift-mirror/tree/." "$LOCAL_MIRROR_ROOT/"
  (
    cd "$LOCAL_MIRROR_ROOT"
    git init -q
    git add -A
    git -c user.name=smoke -c user.email=smoke@local commit -qm "staged mirror tree"
    git tag v0.1.0
  ) || fail "could not turn the staged tree into a taggable local repo"
  # The staged repo's history changes every run, so drop SwiftPM's recorded
  # tag fingerprint for this identity before re-resolving.
  rm -f "$HOME/.swiftpm/security/fingerprints/local-package-repo-"*.json 2>/dev/null
  (
    cd "$SAMPLE_DIR"
    rm -rf .build Package.resolved
    swift package config set-mirror --original "$MIRROR_URL" --mirror "file://$LOCAL_MIRROR_ROOT"
  ) || fail "could not configure the SwiftPM mirror redirection"
else
  unset_mirror
  (cd "$SAMPLE_DIR" && rm -rf .build Package.resolved)
fi

if [[ ! -s "$MODULE" || ! -s "$CONFIG" ]]; then
  log "building Swift WASM module + manifest"
  if ! bash "$SAMPLE_DIR/scripts/build-wasm.sh"; then
    if [[ "$MODE" == "mirror-resolved" ]]; then
      fail "could not build against the public mirror; is github.com/J-Tech-Japan/sekiban-swift public with tag v0.1.0?"
    fi
    skip "could not build the Swift WASM module; install the Swift wasm SDK (swift-6.3.1-RELEASE_wasm) and re-run."
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

http_post() { curlq -s --max-time 60 -w '\n%{http_code}' -H 'Content-Type: application/json' -X POST -d "$2" "$RUNTIME_URL$1"; }

forecast_id="$(python3 - <<'PY'
import uuid
print(uuid.uuid4())
PY
)"
tag="weather:${forecast_id}"
created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

payload_b64=$(printf '{"forecastId":"%s","location":"Kyoto","temperatureC":21,"summary":"swift spm sample","createdAt":"%s"}' "$forecast_id" "$created_at" | base64 | tr -d '\n')
commit_body=$(printf '{"eventCandidates":[{"payload":"%s","eventPayloadName":"WeatherForecastCreated","tags":["%s"]}],"consistencyTags":[]}' "$payload_b64" "$tag")

log "commit WeatherForecastCreated (tag=$tag)"
out=$(http_post "/api/sekiban/serialized/commit" "$commit_body"); code=$(printf '%s' "$out" | tail -n1)
LAST_HTTP_BODY="$(printf '%s' "$out" | sed '$d')"
[[ "$code" == "200" ]] || fail "commit returned HTTP $code: ${LAST_HTTP_BODY}"

update_b64=$(printf '{"forecastId":"%s","newLocation":"Osaka","updatedAt":"%s"}' "$forecast_id" "$created_at" | base64 | tr -d '\n')
update_body=$(printf '{"eventCandidates":[{"payload":"%s","eventPayloadName":"WeatherForecastLocationUpdated","tags":["%s"]}],"consistencyTags":[]}' "$update_b64" "$tag")

log "commit WeatherForecastLocationUpdated (Kyoto -> Osaka)"
out=$(http_post "/api/sekiban/serialized/commit" "$update_body"); code=$(printf '%s' "$out" | tail -n1)
LAST_HTTP_BODY="$(printf '%s' "$out" | sed '$d')"
[[ "$code" == "200" ]] || fail "update commit returned HTTP $code: ${LAST_HTTP_BODY}"
log "command execution OK"

log "tag-state readback (tag-latest-sortable)"
out=$(http_post "/api/sekiban/serialized/tag-latest-sortable" "$(printf '{"tag":"%s"}' "$tag")")
resp=$(printf '%s' "$out" | sed '$d')
printf '%s' "$resp" | python3 -c 'import json,sys; d=json.load(sys.stdin); sys.exit(0 if d.get("exists") else 1)' \
  || fail "tag-state readback did not reflect the committed events: $resp"
log "tag-state readback OK"

log "in-memory projection query (GetWeatherForecastListQuery, expect updated location)"
query_found=0
for _ in $(seq 1 20); do
  out=$(http_post "/api/sekiban/serialized/list-query" '{"queryType":"GetWeatherForecastListQuery","queryParamsJson":"{}"}')
  resp=$(printf '%s' "$out" | sed '$d')
  if printf '%s' "$resp" | grep -q "$forecast_id" && printf '%s' "$resp" | grep -q "Osaka"; then query_found=1; break; fi
  sleep 2
done
[[ "$query_found" == "1" ]] || fail "list-query did not return the updated forecast within timeout: ${resp:0:200}"
log "in-memory projection query OK"

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

write_report "PASS" "Committed WeatherForecastCreated + WeatherForecastLocationUpdated through the public GHCR runtime running the sekiban-swift-built module, confirmed tag-latest-sortable readback, saw the updated location in GetWeatherForecastListQuery, and the WeatherForecast MV caught up in DcbMaterializedViewPostgres ($MV_DETAIL)."
log "PASS ($MODE_DETAIL)"
exit 0
