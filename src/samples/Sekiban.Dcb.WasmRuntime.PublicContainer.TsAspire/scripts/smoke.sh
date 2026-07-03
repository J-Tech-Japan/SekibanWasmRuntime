#!/usr/bin/env bash
# TypeScript AppHost smoke for the public runtime container (SWR-G067).
#
# Packs @sekiban/aspire and installs the sample against the packed tarball
# (never the workspace sources), starts the Aspire TypeScript AppHost
# (apphost.mts) headlessly via `aspire run`, and proves the public GHCR runtime
# container answers health/ready plus a basic command/query round trip
# (commit WeatherForecastCreated -> tag-latest-sortable readback -> list-query).
#
# Writes reports/smoke/public-container-ts-aspire-smoke.md (PASS / FAIL / SKIP).
# Exit 0 on PASS or SKIP (prereq missing), 1 on FAIL.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="$ROOT/src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.TsAspire"
HELPER_DIR="$ROOT/src/lib/sekiban-aspire-ts"
CS_SAMPLE_DIR="$ROOT/src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider"
ARTIFACT_DIR="$ROOT/artifacts/samples/public-container-cs-decider"
MODULE="$ARTIFACT_DIR/modules/public-container-cs-decider.wasm"
CONFIG="$ARTIFACT_DIR/config/sekiban-manifest.json"
REPORT_DIR="$ROOT/reports/smoke"
REPORT="$REPORT_DIR/public-container-ts-aspire-smoke.md"
TIMEOUT="${SAMPLE_SMOKE_TIMEOUT:-300}"
APPHOST_PID=""
APPHOST_LOG="$(mktemp)"

log() { printf '[ts-aspire-smoke] %s\n' "$*"; }
curlq() { curl -q "$@"; }

write_report() {
  local result="$1" detail="$2"
  mkdir -p "$REPORT_DIR"
  {
    printf '# Public Container TS Aspire AppHost Smoke (SWR-G067)\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Runtime image: \`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:${SAMPLE_RUNTIME_IMAGE_TAG:-1.0.0-preview.3}\`"
    printf '%s\n' "- AppHost: Aspire TypeScript apphost.mts + @sekiban/aspire (packed tarball)"
    printf '%s\n' "- Runtime URL: \`${RUNTIME_URL:-unresolved}\`"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
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

command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1 || skip "Docker is not available (the runtime container needs it)."
command -v node >/dev/null 2>&1 || skip "node not found."
command -v aspire >/dev/null 2>&1 || skip "aspire CLI not found (the TypeScript AppHost runs via 'aspire run')."

if [[ ! -s "$MODULE" || ! -s "$CONFIG" ]]; then
  log "building the shared WASM module + manifest (CsDecider public-container sample)"
  if ! bash "$CS_SAMPLE_DIR/scripts/build-wasm.sh"; then
    skip "could not build the WASM module (see build output); set up the WASI build and re-run."
  fi
fi

# ---------------------------------------------------------------------------
# Consume @sekiban/aspire as a packed tarball (lane-verifiable), never as the
# workspace sources: install dependencies, then overlay the tarball with
# --no-save so package.json keeps its dev-time file: reference.
# ---------------------------------------------------------------------------
log "packing @sekiban/aspire"
npm --prefix "$HELPER_DIR" install --no-audit --no-fund >/dev/null 2>&1 || fail "npm install failed for @sekiban/aspire"
HELPER_TGZ_DIR="$(mktemp -d)"
HELPER_TGZ_NAME="$(cd "$HELPER_DIR" && npm pack --pack-destination "$HELPER_TGZ_DIR" --silent 2>/dev/null | tail -n1)"
HELPER_TGZ="$HELPER_TGZ_DIR/$HELPER_TGZ_NAME"
[[ -s "$HELPER_TGZ" ]] || fail "npm pack produced no tarball for @sekiban/aspire"

log "installing sample dependencies + packed helper tarball"
npm --prefix "$SAMPLE_DIR" install --no-audit --no-fund >/dev/null 2>&1 || fail "npm install failed for the sample"
npm --prefix "$SAMPLE_DIR" install --no-save --no-audit --no-fund "$HELPER_TGZ" >/dev/null 2>&1 || fail "installing the packed @sekiban/aspire tarball failed"
if [[ ! -f "$SAMPLE_DIR/node_modules/@sekiban/aspire/dist/index.js" ]]; then
  fail "packed @sekiban/aspire did not install into the sample"
fi

free_port() { python3 -c 'import socket;s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()'; }
RUNTIME_PORT="$(free_port)"
RUNTIME_URL="http://localhost:${RUNTIME_PORT}"
export SAMPLE_RUNTIME_HOST_PORT="$RUNTIME_PORT"
log "runtime host port=$RUNTIME_PORT"

export ASPIRE_ALLOW_UNSECURED_TRANSPORT=true

log "starting Aspire TypeScript AppHost (aspire run: Postgres + public runtime container)"
(cd "$SAMPLE_DIR" && aspire run --non-interactive > "$APPHOST_LOG" 2>&1) &
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
[[ "$ready" == "1" ]] || fail "runtime did not become READY within ${TIMEOUT}s: ${LAST_HTTP_BODY:0:300}"
log "runtime ready"

http_post() { curlq -s --max-time 60 -w '\n%{http_code}' -H 'Content-Type: application/json' -X POST -d "$2" "$RUNTIME_URL$1"; }

forecast_id="ts-aspire-$(date +%Y%m%d%H%M%S)-${RANDOM}"
tag="weather:${forecast_id}"
created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
payload_b64=$(printf '{"forecastId":"%s","location":"Kyoto","temperatureC":21,"summary":"TsAspire","createdAt":"%s"}' "$forecast_id" "$created_at" | base64 | tr -d '\n')
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
for _ in $(seq 1 20); do
  out=$(http_post "/api/sekiban/serialized/list-query" '{"queryType":"GetWeatherForecastListQuery","queryParamsJson":"{}"}')
  resp=$(printf '%s' "$out" | sed '$d')
  if printf '%s' "$resp" | grep -q "$forecast_id"; then query_found=1; break; fi
  sleep 2
done
[[ "$query_found" == "1" ]] || fail "list-query did not return the committed forecast within timeout: ${resp:0:200}"
log "list-query OK"

write_report "PASS" "Aspire TypeScript AppHost (apphost.mts + packed @sekiban/aspire tarball) started the public runtime container; health/ready OK; committed WeatherForecastCreated ($forecast_id), read it back via tag-latest-sortable, and saw it in GetWeatherForecastListQuery."
log "PASS"
exit 0
