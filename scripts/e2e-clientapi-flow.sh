#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

E2E_SAMPLE="${E2E_SAMPLE:-cs}"
case "$E2E_SAMPLE" in
  cs)
    APPHOST_PROJ="$ROOT/src/internalUsages/cs/SekibanWasm.Cs.AppHost/SekibanWasm.Cs.AppHost.csproj"
    BUILD_WASM_SCRIPT="$ROOT/build/scripts/build-csharp-wasm.sh"
    WASM_MODULE="$ROOT/src/internalUsages/cs/modules/csharp-weather.wasm"
    ;;
  rust)
    APPHOST_PROJ="$ROOT/src/internalUsages/rust/SekibanWasm.Rust.AppHost/SekibanWasm.Rust.AppHost.csproj"
    BUILD_WASM_SCRIPT="$ROOT/build/scripts/build-rust-wasm.sh"
    WASM_MODULE="$ROOT/src/internalUsages/rust/modules/rust-weather.wasm"
    ;;
  *)
    echo "[e2e-flow] ERROR: unknown E2E_SAMPLE='$E2E_SAMPLE' (expected: cs|rust)" >&2
    exit 2
    ;;
esac

E2E_TIMEOUT_SECONDS="${E2E_TIMEOUT_SECONDS:-180}"

pick_free_port() {
  python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
}

json_read() {
  local file="$1"
  local expr="$2"
  python3 - "$file" "$expr" <<'PY'
import json, sys
path = sys.argv[1]
expr = sys.argv[2]
with open(path, "r", encoding="utf-8") as f:
    data = json.load(f)
value = eval(expr, {"__builtins__": {}}, {"data": data})
if value is None:
    raise SystemExit(1)
if isinstance(value, bool):
    print("true" if value else "false")
elif isinstance(value, (dict, list)):
    print(json.dumps(value))
else:
    print(value)
PY
}

assert_json_predicate() {
  local file="$1"
  local predicate="$2"
  local message="$3"
  python3 - "$file" "$predicate" "$message" <<'PY'
import json, sys
path, predicate, message = sys.argv[1:4]
with open(path, "r", encoding="utf-8") as f:
    data = json.load(f)
safe_builtins = {"any": any, "all": all, "len": len}
ok = eval(predicate, {"__builtins__": safe_builtins}, {"data": data})
if not ok:
    print(message, file=sys.stderr)
    print(json.dumps(data, ensure_ascii=False, indent=2), file=sys.stderr)
    raise SystemExit(1)
PY
}

request_json() {
  local method="$1"
  local url="$2"
  local body="${3:-}"
  local out_file="$4"
  python3 - "$method" "$url" "$body" "$out_file" <<'PY'
import json, sys, urllib.request, urllib.error
method, url, body, out_file = sys.argv[1:5]
data = body.encode("utf-8") if body else None
headers = {"Content-Type": "application/json"} if body else {}
request = urllib.request.Request(url, data=data, headers=headers, method=method)
try:
    with urllib.request.urlopen(request, timeout=20) as response:
        content = response.read()
        with open(out_file, "wb") as f:
            f.write(content)
        print(response.status)
except urllib.error.HTTPError as error:
    content = error.read()
    with open(out_file, "wb") as f:
        f.write(content)
    print(error.code)
except Exception as error:
    print(f"[e2e-flow] request failed for {method} {url}: {error}", file=sys.stderr)
    raise
PY
}

assert_http_success() {
  local status="$1"
  local label="$2"
  local file="$3"
  if [[ ! "$status" =~ ^2 ]]; then
    echo "[e2e-flow] FAIL: ${label} returned HTTP ${status}" >&2
    cat "$file" >&2 || true
    exit 1
  fi
}

wait_for_ready() {
  local url="$1"
  local deadline=$((SECONDS + E2E_TIMEOUT_SECONDS))
  until curl -fsS --max-time 2 "$url" >/dev/null 2>&1; do
    if (( SECONDS > deadline )); then
      echo "[e2e-flow] Timeout waiting for $url" >&2
      tail -n 200 "$APPHOST_LOG" >&2 || true
      exit 1
    fi
    sleep 2
  done
}

extract_sortable_id() {
  local file="$1"
  python3 - "$file" <<'PY'
import json, sys
with open(sys.argv[1], "r", encoding="utf-8") as f:
    data = json.load(f)
value = data.get("sortableUniqueId")
if not value:
    written = data.get("writtenEvents") or []
    if written:
        value = written[0].get("sortableUniqueIdValue")
if not value:
    raise SystemExit(1)
print(value)
PY
}

if [[ ! -f "$WASM_MODULE" ]]; then
  echo "[e2e-flow] WASM module not found. Building via: $BUILD_WASM_SCRIPT"
  "$BUILD_WASM_SCRIPT"
fi

E2E_API_PORT="${E2E_API_PORT:-$(pick_free_port)}"
E2E_CLIENT_API_PORT="${E2E_CLIENT_API_PORT:-$(pick_free_port)}"
APPHOST_PORT="${APPHOST_PORT:-$(pick_free_port)}"
RESOURCE_SERVICE_PORT="${RESOURCE_SERVICE_PORT:-$(pick_free_port)}"
OTLP_PORT="${OTLP_PORT:-$(pick_free_port)}"
CLIENT_API_BASE_URL="${CLIENT_API_BASE_URL:-http://127.0.0.1:${E2E_CLIENT_API_PORT}}"

TS="$(date +%Y%m%d-%H%M%S)"
ARTIFACT_DIR="$ROOT/artifacts/e2e-clientapi/$TS"
mkdir -p "$ARTIFACT_DIR"
APPHOST_LOG="$ARTIFACT_DIR/apphost.log"

cleanup() {
  if [[ -n "${APPHOST_PID:-}" ]] && kill -0 "$APPHOST_PID" 2>/dev/null; then
    kill "$APPHOST_PID" 2>/dev/null || true
    sleep 2 || true
    kill -9 "$APPHOST_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

APPHOST_ENV=("E2E_API_PORT=$E2E_API_PORT")
APPHOST_ENV+=("E2E_CLIENT_API_PORT=$E2E_CLIENT_API_PORT")
APPHOST_ENV+=("ASPNETCORE_URLS=http://127.0.0.1:${APPHOST_PORT}")
APPHOST_ENV+=("DOTNET_RESOURCE_SERVICE_ENDPOINT_URL=http://127.0.0.1:${RESOURCE_SERVICE_PORT}")
APPHOST_ENV+=("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL=http://127.0.0.1:${OTLP_PORT}")
APPHOST_ENV+=("ASPIRE_ALLOW_UNSECURED_TRANSPORT=true")
APPHOST_ENV+=("SEKIBAN_PROJECTION_RUNTIME=wasm")
APPHOST_ENV+=("WASM_MODULE_PATH=$WASM_MODULE")

echo "[e2e-flow] Starting Aspire AppHost: $APPHOST_PROJ"
(
  cd "$ROOT"
  env "${APPHOST_ENV[@]}" dotnet run --no-launch-profile --project "$APPHOST_PROJ"
) >"$APPHOST_LOG" 2>&1 &
APPHOST_PID="$!"

echo "[e2e-flow] Waiting for ClientApi: ${CLIENT_API_BASE_URL}/api/weatherforecast"
wait_for_ready "${CLIENT_API_BASE_URL}/api/weatherforecast"

FORECAST_ID="$(python3 - <<'PY'
import uuid
print(uuid.uuid4())
PY
)"
CREATE_FILE="$ARTIFACT_DIR/create.json"
LIST_AFTER_CREATE_FILE="$ARTIFACT_DIR/list-after-create.json"
UPDATE_FILE="$ARTIFACT_DIR/update.json"
LIST_AFTER_UPDATE_FILE="$ARTIFACT_DIR/list-after-update.json"
DELETE_FILE="$ARTIFACT_DIR/delete.json"
LIST_AFTER_DELETE_FILE="$ARTIFACT_DIR/list-after-delete.json"

CREATE_PAYLOAD="$(python3 - "$FORECAST_ID" <<'PY'
import json, sys
print(json.dumps({
    "forecastId": sys.argv[1],
    "location": "Tokyo",
    "temperatureC": 25,
    "summary": "Sunny",
}))
PY
)"

echo "[e2e-flow] POST create forecast ${FORECAST_ID}"
CREATE_STATUS=$(request_json POST "${CLIENT_API_BASE_URL}/api/weatherforecast" "$CREATE_PAYLOAD" "$CREATE_FILE")
assert_http_success "$CREATE_STATUS" "create forecast" "$CREATE_FILE"
CREATE_SORTABLE_ID="$(extract_sortable_id "$CREATE_FILE")"

echo "[e2e-flow] GET list after create"
LIST_CREATE_STATUS=$(request_json GET "${CLIENT_API_BASE_URL}/api/weatherforecast?waitForSortableId=${CREATE_SORTABLE_ID}" "" "$LIST_AFTER_CREATE_FILE")
assert_http_success "$LIST_CREATE_STATUS" "list after create" "$LIST_AFTER_CREATE_FILE"
assert_json_predicate \
  "$LIST_AFTER_CREATE_FILE" \
  "any(item.get('forecastId') == '${FORECAST_ID}' and item.get('location') == 'Tokyo' for item in data)" \
  "created forecast not found in list result"

UPDATE_PAYLOAD="$(python3 - "$FORECAST_ID" <<'PY'
import json, sys
print(json.dumps({
    "forecastId": sys.argv[1],
    "newLocation": "Osaka",
}))
PY
)"

echo "[e2e-flow] POST update location"
UPDATE_STATUS=$(request_json POST "${CLIENT_API_BASE_URL}/api/weatherforecast/update-location" "$UPDATE_PAYLOAD" "$UPDATE_FILE")
assert_http_success "$UPDATE_STATUS" "update location" "$UPDATE_FILE"
UPDATE_SORTABLE_ID="$(extract_sortable_id "$UPDATE_FILE")"

echo "[e2e-flow] GET list after update"
LIST_UPDATE_STATUS=$(request_json GET "${CLIENT_API_BASE_URL}/api/weatherforecast?waitForSortableId=${UPDATE_SORTABLE_ID}" "" "$LIST_AFTER_UPDATE_FILE")
assert_http_success "$LIST_UPDATE_STATUS" "list after update" "$LIST_AFTER_UPDATE_FILE"
assert_json_predicate \
  "$LIST_AFTER_UPDATE_FILE" \
  "any(item.get('forecastId') == '${FORECAST_ID}' and item.get('location') == 'Osaka' for item in data)" \
  "updated forecast location not found in list result"

DELETE_PAYLOAD="$(python3 - "$FORECAST_ID" <<'PY'
import json, sys
print(json.dumps({
    "forecastId": sys.argv[1],
}))
PY
)"

echo "[e2e-flow] POST delete forecast"
DELETE_STATUS=$(request_json POST "${CLIENT_API_BASE_URL}/api/weatherforecast/delete" "$DELETE_PAYLOAD" "$DELETE_FILE")
assert_http_success "$DELETE_STATUS" "delete forecast" "$DELETE_FILE"
DELETE_SORTABLE_ID="$(extract_sortable_id "$DELETE_FILE")"

echo "[e2e-flow] GET list after delete"
LIST_DELETE_STATUS=$(request_json GET "${CLIENT_API_BASE_URL}/api/weatherforecast?waitForSortableId=${DELETE_SORTABLE_ID}" "" "$LIST_AFTER_DELETE_FILE")
assert_http_success "$LIST_DELETE_STATUS" "list after delete" "$LIST_AFTER_DELETE_FILE"
assert_json_predicate \
  "$LIST_AFTER_DELETE_FILE" \
  "all(item.get('forecastId') != '${FORECAST_ID}' for item in data)" \
  "deleted forecast still present in list result"

echo "[e2e-flow] OK (artifacts: $ARTIFACT_DIR)"
