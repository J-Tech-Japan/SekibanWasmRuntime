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

write_report() {
  local result="$1" detail="$2"
  mkdir -p "$REPORT_DIR"
  {
    printf '# Public Container CS Decider Smoke (SWR-G036)\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Runtime image: \`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1\`"
    printf '%s\n' "- Runtime URL: \`${RUNTIME_URL:-unresolved}\`"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
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
  log "FAIL: $*"; write_report "FAIL" "$*"; exit 1
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
  code=$(curl -s -o /dev/null --max-time 5 -w '%{http_code}' "$RUNTIME_URL/health" || true)
  if [[ "$code" == "200" ]]; then healthy=1; break; fi
  sleep 5
done
[[ "$healthy" == "1" ]] || fail "runtime did not become healthy within ${TIMEOUT}s"
log "runtime healthy"

root_resp=$(curl -s --max-time 10 "$RUNTIME_URL/" || true)
printf '%s' "$root_resp" | grep -q "Sekiban WASM Runtime Host" || fail "service at $RUNTIME_URL is not the Sekiban runtime: ${root_resp:0:200}"
log "runtime identity confirmed"

http_post() { curl -s --max-time 60 -w '\n%{http_code}' -H 'Content-Type: application/json' -X POST -d "$2" "$RUNTIME_URL$1"; }

forecast_id="sample-$(date +%Y%m%d%H%M%S)-${RANDOM}"
tag="weather:${forecast_id}"
created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
payload_b64=$(printf '{"forecastId":"%s","location":"Kyoto","temperatureC":24,"summary":"Sample","createdAt":"%s"}' "$forecast_id" "$created_at" | base64 | tr -d '\n')
commit_body=$(printf '{"eventCandidates":[{"payload":"%s","eventPayloadName":"WeatherForecastCreated","tags":["%s"]}],"consistencyTags":[]}' "$payload_b64" "$tag")

log "commit WeatherForecastCreated (tag=$tag)"
out=$(http_post "/api/sekiban/serialized/commit" "$commit_body"); code=$(printf '%s' "$out" | tail -n1)
[[ "$code" == "200" ]] || fail "commit returned HTTP $code: $(printf '%s' "$out" | sed '$d')"
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

log "PASS: commit + tag-state read + list-query all succeeded through the public runtime container"
write_report "PASS" "Committed WeatherForecastCreated (tag=$tag), read it back via tag-latest-sortable, and saw it in GetWeatherForecastListQuery — all through ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1."
exit 0
