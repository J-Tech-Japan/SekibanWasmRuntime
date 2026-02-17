#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

E2E_SAMPLE="${E2E_SAMPLE:-cs}"
case "$E2E_SAMPLE" in
  cs)
    APPHOST_PROJ="$ROOT/src/internalUsages/cs/SekibanWasm.Cs.AppHost/SekibanWasm.Cs.AppHost.csproj"
    ;;
  rust)
    APPHOST_PROJ="$ROOT/src/internalUsages/rust/SekibanWasm.Rust.AppHost/SekibanWasm.Rust.AppHost.csproj"
    ;;
  *)
    echo "[e2e] ERROR: unknown E2E_SAMPLE='$E2E_SAMPLE' (expected: cs|rust)" >&2
    exit 2
    ;;
esac
READY_URL_PATH="${READY_URL_PATH:-/openapi/v1.json}"
E2E_TIMEOUT_SECONDS="${E2E_TIMEOUT_SECONDS:-180}"
E2E_REQUIRE_POST="${E2E_REQUIRE_POST:-0}"

pick_free_port() {
  python3 - <<'PY'
import socket
s = socket.socket()
s.bind(("127.0.0.1", 0))
print(s.getsockname()[1])
s.close()
PY
}

E2E_API_PORT="${E2E_API_PORT:-$(pick_free_port)}"
API_BASE_URL="${API_BASE_URL:-http://127.0.0.1:${E2E_API_PORT}}"
APPHOST_PORT="${APPHOST_PORT:-$(pick_free_port)}"
RESOURCE_SERVICE_PORT="${RESOURCE_SERVICE_PORT:-$(pick_free_port)}"
OTLP_PORT="${OTLP_PORT:-$(pick_free_port)}"

TS="$(date +%Y%m%d-%H%M%S)"
ARTIFACT_DIR="$ROOT/artifacts/e2e/$TS"
mkdir -p "$ARTIFACT_DIR"

APPHOST_LOG="$ARTIFACT_DIR/apphost.log"
API_HEALTH_LOG="$ARTIFACT_DIR/api-curl.log"

# Internal usages always run with the WASM projection runtime.
case "$E2E_SAMPLE" in
  cs)
    BUILD_WASM_SCRIPT="$ROOT/build/scripts/build-csharp-wasm.sh"
    WASM_MODULE="$ROOT/src/internalUsages/cs/modules/csharp-weather.wasm"
    ;;
  rust)
    BUILD_WASM_SCRIPT="$ROOT/build/scripts/build-rust-wasm.sh"
    WASM_MODULE="$ROOT/src/internalUsages/rust/modules/rust-weather.wasm"
    ;;
esac

if [[ ! -f "$WASM_MODULE" ]]; then
  echo "[e2e] WASM module not found. Building via: $BUILD_WASM_SCRIPT"
  "$BUILD_WASM_SCRIPT"
fi

cleanup() {
  if [[ -n "${APPHOST_PID:-}" ]] && kill -0 "$APPHOST_PID" 2>/dev/null; then
    kill "$APPHOST_PID" 2>/dev/null || true
    sleep 2 || true
    kill -9 "$APPHOST_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

# Prepare environment variables for AppHost
APPHOST_ENV=("E2E_API_PORT=$E2E_API_PORT")
# Avoid port collisions between repeated E2E runs (Aspire defaults are fixed in launchSettings.json).
APPHOST_ENV+=("ASPNETCORE_URLS=http://127.0.0.1:${APPHOST_PORT}")
APPHOST_ENV+=("DOTNET_RESOURCE_SERVICE_ENDPOINT_URL=http://127.0.0.1:${RESOURCE_SERVICE_PORT}")
APPHOST_ENV+=("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL=http://127.0.0.1:${OTLP_PORT}")
# Required when running without the launch profile (we use http:// endpoints).
APPHOST_ENV+=("ASPIRE_ALLOW_UNSECURED_TRANSPORT=true")
echo "[e2e] Projection runtime: WASM"
APPHOST_ENV+=("SEKIBAN_PROJECTION_RUNTIME=wasm")
APPHOST_ENV+=("WASM_MODULE_PATH=$WASM_MODULE")

echo "[e2e] Starting Aspire AppHost: $APPHOST_PROJ"
(
  cd "$ROOT"
  # Avoid fixed ports coming from Properties/launchSettings.json.
  env "${APPHOST_ENV[@]}" dotnet run --no-launch-profile --project "$APPHOST_PROJ"
) >"$APPHOST_LOG" 2>&1 &
APPHOST_PID="$!"

echo "[e2e] Waiting for API to be ready: ${API_BASE_URL}${READY_URL_PATH}"
deadline=$((SECONDS + E2E_TIMEOUT_SECONDS))
until curl -fsS --max-time 2 "${API_BASE_URL}${READY_URL_PATH}" >"$API_HEALTH_LOG" 2>&1; do
  if (( SECONDS > deadline )); then
    echo "[e2e] Timeout waiting for API. See: $APPHOST_LOG"
    tail -n 200 "$APPHOST_LOG" >"$ARTIFACT_DIR/apphost-tail.txt" || true
    exit 1
  fi
  sleep 2
done

echo "[e2e] Smoke check: GET ${READY_URL_PATH}"
cat "$API_HEALTH_LOG"

# POST smoke check: create a weather forecast
POST_LOG="$ARTIFACT_DIR/api-post.log"
POST_ERR_LOG="$ARTIFACT_DIR/api-post.err.log"
set +e
HTTP_STATUS=$(curl --config /dev/null -sS -o "$POST_LOG" -w '%{http_code}' \
  -X POST "${API_BASE_URL}/api/weatherforecast" \
  -H 'Content-Type: application/json' \
  -d '{"forecastId":"e2e-test-1","location":"Tokyo","temperatureC":25,"summary":"Sunny"}' \
  --max-time 10 2>"$POST_ERR_LOG")
CURL_EXIT=$?
set -e
if [[ -z "$HTTP_STATUS" ]]; then
  HTTP_STATUS="000"
fi

echo "[e2e] POST /api/weatherforecast => HTTP $HTTP_STATUS"
if [[ -f "$POST_LOG" ]]; then
  cat "$POST_LOG"
  echo
fi
if [[ -s "$POST_ERR_LOG" ]]; then
  cat "$POST_ERR_LOG"
fi

if [[ "$E2E_REQUIRE_POST" == "1" ]] && [[ ! "$HTTP_STATUS" =~ ^2 ]]; then
  echo "[e2e] FAIL: POST /api/weatherforecast returned HTTP $HTTP_STATUS (E2E_REQUIRE_POST=1)"
  [[ -f "$POST_LOG" ]] && cat "$POST_LOG"
  exit 1
fi

if [[ "$E2E_REQUIRE_POST" != "1" ]] && [[ ! "$HTTP_STATUS" =~ ^2 ]]; then
  echo "[e2e] WARN: POST /api/weatherforecast returned HTTP $HTTP_STATUS (non-blocking)"
fi

echo "[e2e] OK (artifacts: $ARTIFACT_DIR)"
