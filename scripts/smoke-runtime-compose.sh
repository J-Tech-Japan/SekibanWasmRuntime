#!/usr/bin/env bash
#
# Local runtime compose smoke (SWR-G031).
#
# Proves the Sekiban WASM Runtime Host container works as a real event-sourced
# backend: it starts external Postgres + the runtime container (with a mounted
# manifest and WASM module), commits a serialized event through the container,
# then reads it back through the container. Exactly one command runs the whole
# thing and prints a clear PASS / FAIL / SKIP result.
#
# Usage:
#   bash scripts/smoke-runtime-compose.sh
#
# Behavior / environment knobs:
#   SMOKE_WASM_MODULE   Path to a .wasm projector module to mount. If unset, the
#                       script reuses an existing module or builds the C# weather
#                       sample. If none can be obtained -> SKIP.
#   SMOKE_ENGINE        Force container engine: docker | podman (auto-detected).
#   SMOKE_RUNTIME_URL   Runtime base URL (default http://localhost:3000).
#   SMOKE_HEALTH_TIMEOUT  Seconds to wait for /health (default 180).
#   SMOKE_SKIP_BUILD=1  Do not try to build a WASM module if none is found.
#   SMOKE_KEEP_UP=1     Do not tear the stack down on exit (for debugging).
#
# Exit codes: 0 = PASS or SKIP (prereqs unavailable), 1 = FAIL.

set -uo pipefail

cd "$(git rev-parse --show-toplevel)" || {
  echo "[smoke] ERROR: not inside a git repository" >&2
  exit 1
}

REPO_ROOT="$(pwd)"
COMPOSE_DIR="$REPO_ROOT/docker/sekiban-wasm-runtime"
COMPOSE_FILE="$COMPOSE_DIR/docker-compose.yml"
MODULE_DEST="$COMPOSE_DIR/modules/weather.wasm"
PROJECT_NAME="sekiban-wasm-smoke"
RUNTIME_URL=""     # resolved from a free host port (or SMOKE_RUNTIME_URL)
HEALTH_TIMEOUT="${SMOKE_HEALTH_TIMEOUT:-180}"
REPORT_DIR="$REPO_ROOT/reports/smoke"
REPORT_FILE="$REPORT_DIR/runtime-compose-smoke.md"

COMPOSE=()         # resolved compose command (array)
ENGINE=""          # resolved engine binary
COPIED_MODULE=0    # whether we copied a module into the compose dir

log()  { printf '[smoke] %s\n' "$*"; }
err()  { printf '[smoke] ERROR: %s\n' "$*" >&2; }

skip() {
  log "SKIP: $*"
  write_report "SKIP" "$*"
  exit 0
}

fail() {
  err "FAIL: $*"
  capture_failure_logs
  write_report "FAIL" "$*"
  exit 1
}

write_report() {
  local result="$1" detail="$2"
  mkdir -p "$REPORT_DIR"
  {
    printf '# Runtime Compose Smoke (SWR-G031)\n\n'
    printf -- '- Result: **%s**\n' "$result"
    printf -- '- Detail: %s\n' "$detail"
    printf -- '- Engine: `%s`\n' "${ENGINE:-unresolved}"
    printf -- '- Compose: `%s`\n' "${COMPOSE[*]:-unresolved}"
    printf -- '- Runtime URL: `%s`\n' "$RUNTIME_URL"
    printf -- '- Commit: `%s`\n' "$(git rev-parse HEAD 2>/dev/null || echo unknown)"
    if [[ -n "${SMOKE_LOG_TAIL:-}" ]]; then
      printf '\n## Container logs (tail)\n\n```\n%s\n```\n' "$SMOKE_LOG_TAIL"
    fi
  } > "$REPORT_FILE"
  log "report: ${REPORT_FILE#$REPO_ROOT/}"
}

capture_failure_logs() {
  if [[ ${#COMPOSE[@]} -gt 0 ]]; then
    SMOKE_LOG_TAIL="$(compose logs --no-color --tail 200 2>&1 | tail -200)"
  fi
}

compose() {
  ( cd "$COMPOSE_DIR" && "${COMPOSE[@]}" -p "$PROJECT_NAME" "$@" )
}

cleanup() {
  if [[ "${SMOKE_KEEP_UP:-0}" == "1" ]]; then
    log "SMOKE_KEEP_UP=1 — leaving stack running (tear down with: cd $COMPOSE_DIR && ${COMPOSE[*]} -p $PROJECT_NAME down -v)"
  elif [[ ${#COMPOSE[@]} -gt 0 ]]; then
    log "tearing down stack"
    compose down -v >/dev/null 2>&1 || true
  fi
  if [[ "$COPIED_MODULE" == "1" ]]; then
    rm -f "$MODULE_DEST"
  fi
}
trap cleanup EXIT

# --- 1. Resolve container engine + compose ----------------------------------

resolve_engine() {
  local candidates=()
  if [[ -n "${SMOKE_ENGINE:-}" ]]; then
    candidates=("$SMOKE_ENGINE")
  else
    candidates=(docker podman)
  fi
  for c in "${candidates[@]}"; do
    if command -v "$c" >/dev/null 2>&1 && "$c" info >/dev/null 2>&1; then
      ENGINE="$c"
      break
    fi
  done
  [[ -n "$ENGINE" ]] || skip "no usable container engine (docker/podman daemon not available)"

  if "$ENGINE" compose version >/dev/null 2>&1; then
    COMPOSE=("$ENGINE" compose)
  elif [[ "$ENGINE" == "docker" ]] && command -v docker-compose >/dev/null 2>&1; then
    COMPOSE=(docker-compose)
  else
    skip "no compose plugin available for '$ENGINE'"
  fi
  log "engine=$ENGINE compose='${COMPOSE[*]}'"
}

# --- 1b. Pick free host ports (avoid conflicts with e.g. a running Aspire) --

pick_free_port() {
  python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
}

resolve_ports() {
  if [[ -n "${SMOKE_RUNTIME_URL:-}" ]]; then
    # Operator pinned an explicit URL; don't publish a competing host port.
    RUNTIME_URL="$SMOKE_RUNTIME_URL"
    log "using operator-provided runtime URL: $RUNTIME_URL"
    return
  fi
  local runtime_port postgres_port
  runtime_port="${SEKIBAN_RUNTIME_PORT:-$(pick_free_port)}"
  postgres_port="${SEKIBAN_POSTGRES_PORT:-$(pick_free_port)}"
  # Exported so the committed compose maps these (ports use ${VAR:-default}).
  export SEKIBAN_RUNTIME_PORT="$runtime_port"
  export SEKIBAN_POSTGRES_PORT="$postgres_port"
  RUNTIME_URL="http://localhost:${runtime_port}"
  log "runtime host port=${runtime_port} (postgres host port=${postgres_port})"
}

# --- 2. Resolve a WASM projector module to mount ----------------------------

resolve_wasm_module() {
  if [[ -s "$MODULE_DEST" ]]; then
    log "using committed module: ${MODULE_DEST#$REPO_ROOT/}"
    return
  fi

  local src=""
  if [[ -n "${SMOKE_WASM_MODULE:-}" ]]; then
    [[ -s "$SMOKE_WASM_MODULE" ]] || fail "SMOKE_WASM_MODULE='$SMOKE_WASM_MODULE' does not exist"
    src="$SMOKE_WASM_MODULE"
  elif [[ -s "$REPO_ROOT/src/internalUsages/cs/modules/csharp-weather.wasm" ]]; then
    src="$REPO_ROOT/src/internalUsages/cs/modules/csharp-weather.wasm"
  elif [[ "${SMOKE_SKIP_BUILD:-0}" != "1" && -x "$REPO_ROOT/build/scripts/build-csharp-wasm.sh" ]]; then
    log "no module found — building C# weather WASM (set SMOKE_SKIP_BUILD=1 to skip)"
    if bash "$REPO_ROOT/build/scripts/build-csharp-wasm.sh" >/tmp/smoke-wasm-build.log 2>&1 \
        && [[ -s "$REPO_ROOT/src/internalUsages/cs/modules/csharp-weather.wasm" ]]; then
      src="$REPO_ROOT/src/internalUsages/cs/modules/csharp-weather.wasm"
    else
      skip "could not build a WASM module (see /tmp/smoke-wasm-build.log); set SMOKE_WASM_MODULE to a prebuilt .wasm"
    fi
  else
    skip "no WASM module available; set SMOKE_WASM_MODULE to a prebuilt .wasm projector"
  fi

  mkdir -p "$(dirname "$MODULE_DEST")"
  cp "$src" "$MODULE_DEST"
  COPIED_MODULE=1
  log "mounted module from ${src#$REPO_ROOT/}"
}

# --- 3. HTTP helpers --------------------------------------------------------

http_post() {
  # $1 path, $2 json body -> echoes "<http_code>\n<body>"
  # -q ignores any ~/.curlrc: a local `--fail` there would swallow the 4xx body this
  # smoke asserts on, making a correct rejection look like a transport error.
  curl -q --silent --show-error --max-time 60 \
    -w '\n%{http_code}' \
    -H 'Content-Type: application/json' \
    -X POST -d "$2" "$RUNTIME_URL$1"
}

json_get() {
  # $1 json, $2 key -> value (empty if missing)
  printf '%s' "$1" | python3 -c 'import json,sys
try:
    d=json.load(sys.stdin)
except Exception:
    sys.exit(0)
v=d.get(sys.argv[1])
if v is None:
    print("")
elif isinstance(v, bool):
    print("true" if v else "false")
elif isinstance(v, (dict, list)):
    print(json.dumps(v))
else:
    print(v)' "$2"
}

# --- 4. Run the smoke -------------------------------------------------------

resolve_engine
resolve_ports
[[ -f "$COMPOSE_FILE" ]] || fail "compose file not found: $COMPOSE_FILE"
resolve_wasm_module

log "starting Postgres + runtime (compose up --build)"
if ! compose up -d --build runtime; then
  fail "compose up failed"
fi

log "waiting up to ${HEALTH_TIMEOUT}s for ${RUNTIME_URL}/health"
healthy=0
deadline=$(( $(date +%s) + HEALTH_TIMEOUT ))
while [[ $(date +%s) -lt $deadline ]]; do
  code=$(curl --silent --output /dev/null --max-time 5 -w '%{http_code}' "$RUNTIME_URL/health" || true)
  if [[ "$code" == "200" ]]; then healthy=1; break; fi
  sleep 3
done
[[ "$healthy" == "1" ]] || fail "runtime did not become healthy within ${HEALTH_TIMEOUT}s"
log "runtime healthy"

# Identity guard: make sure ${RUNTIME_URL} is actually the Sekiban runtime and not
# some other service that happens to occupy the port and answer /health.
root_resp=$(curl --silent --show-error --max-time 10 "$RUNTIME_URL/" || true)
if ! printf '%s' "$root_resp" | grep -q "Sekiban WASM Runtime Host"; then
  fail "service at ${RUNTIME_URL} is not the Sekiban runtime (root response: ${root_resp:0:200})"
fi
log "runtime identity confirmed"

forecast_id="smoke-$(date +%Y%m%d%H%M%S)-${RANDOM}"
tag="weather:${forecast_id}"
created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

payload_json=$(printf '{"forecastId":"%s","location":"Tokyo","temperatureC":21,"summary":"Smoke","createdAt":"%s"}' \
  "$forecast_id" "$created_at")
payload_b64=$(printf '%s' "$payload_json" | base64 | tr -d '\n')

# SWR-G071: the V1 envelope is the legacy official shape plus an explicit `version`
# discriminator. Candidates keep base64 `payload`, `eventPayloadName`, and their own
# per-candidate `tags` -- there is no events/payloadJson/eventType rename and no
# per-commit tags list.
candidates=$(printf '[{"payload":"%s","eventPayloadName":"WeatherForecastCreated","tags":["%s"]}]' \
  "$payload_b64" "$tag")
commit_body=$(printf '{"version":1,"eventCandidates":%s,"consistencyTags":[]}' "$candidates")

log "committing WeatherForecastCreated via V1 envelope (tag=$tag)"
commit_out=$(http_post "/api/sekiban/serialized/commit" "$commit_body")
commit_code=$(printf '%s' "$commit_out" | tail -n1)
commit_resp=$(printf '%s' "$commit_out" | sed '$d')
[[ "$commit_code" == "200" ]] || fail "V1 commit returned HTTP $commit_code: $commit_resp"
log "V1 commit OK (HTTP 200)"

# Mixed-version proof: a pre-10.7 client sends no `version` at all. The host must still
# accept it, otherwise upgrading the runtime silently breaks older consumers.
legacy_id="${forecast_id}-legacy"
legacy_payload_b64=$(printf '{"forecastId":"%s","location":"Osaka","temperatureC":19,"summary":"Legacy","createdAt":"%s"}' \
  "$legacy_id" "$created_at" | base64 | tr -d '\n')
legacy_body=$(printf '{"eventCandidates":[{"payload":"%s","eventPayloadName":"WeatherForecastCreated","tags":["weather:%s"]}],"consistencyTags":[]}' \
  "$legacy_payload_b64" "$legacy_id")
log "committing legacy unversioned envelope (10.2.2-era client shape)"
legacy_out=$(http_post "/api/sekiban/serialized/commit" "$legacy_body")
legacy_code=$(printf '%s' "$legacy_out" | tail -n1)
[[ "$legacy_code" == "200" ]] || fail "legacy unversioned commit returned HTTP $legacy_code: $(printf '%s' "$legacy_out" | sed '$d')"
log "legacy unversioned commit OK (HTTP 200)"

# Fail-closed proof: off-contract envelopes must be rejected before any write, not
# bound optimistically. A silent 200 here would mean events written under a shape the
# runtime does not actually understand.
assert_rejected() {
  local label="$1" body="$2" expected_code="$3"
  local out code resp
  out=$(http_post "/api/sekiban/serialized/commit" "$body")
  code=$(printf '%s' "$out" | tail -n1)
  resp=$(printf '%s' "$out" | sed '$d')
  [[ "$code" == "400" ]] || fail "$label should be rejected with HTTP 400, got HTTP $code: $resp"
  printf '%s' "$resp" | grep -q "$expected_code" \
    || fail "$label rejection should carry code '$expected_code', got: $resp"
  log "$label rejected as expected (HTTP 400, $expected_code)"
}

assert_rejected "unsupported envelope version" \
  "$(printf '{"version":999,"eventCandidates":%s,"consistencyTags":[]}' "$candidates")" \
  "unsupported_commit_envelope_version"
assert_rejected "case-variant version discriminator" \
  "$(printf '{"Version":1,"eventCandidates":%s,"consistencyTags":[]}' "$candidates")" \
  "malformed_commit_envelope"

log "reading back tag latest sortable id (tag=$tag)"
read_out=$(http_post "/api/sekiban/serialized/tag-latest-sortable" "$(printf '{"tag":"%s"}' "$tag")")
read_code=$(printf '%s' "$read_out" | tail -n1)
read_resp=$(printf '%s' "$read_out" | sed '$d')
[[ "$read_code" == "200" ]] || fail "tag-latest-sortable returned HTTP $read_code: $read_resp"

exists=$(json_get "$read_resp" exists)
last_id=$(json_get "$read_resp" lastSortableUniqueId)
if [[ "$exists" != "true" || -z "$last_id" ]]; then
  fail "read-back did not reflect the committed event (response: $read_resp)"
fi

log "read-back OK: tag exists=true, lastSortableUniqueId=$last_id"
log "PASS: committed and read back an event through the runtime container (external Postgres)"
write_report "PASS" "V1 envelope round-trip: committed WeatherForecastCreated (tag=$tag) and read it back via tag-latest-sortable (lastSortableUniqueId=$last_id). Legacy unversioned envelope also accepted (HTTP 200); unsupported version 999 and case-variant \`Version\` both rejected with HTTP 400 before any write."
exit 0
