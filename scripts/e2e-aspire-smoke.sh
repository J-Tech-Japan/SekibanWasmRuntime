#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

APPHOST_PROJ="$ROOT/src/internalUsage/SekibanWasm.AppHost/SekibanWasm.AppHost.csproj"
READY_URL_PATH="${READY_URL_PATH:-/api/weatherforecast}"
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

TS="$(date +%Y%m%d-%H%M%S)"
ARTIFACT_DIR="$ROOT/artifacts/e2e/$TS"
mkdir -p "$ARTIFACT_DIR"

APPHOST_LOG="$ARTIFACT_DIR/apphost.log"
API_HEALTH_LOG="$ARTIFACT_DIR/api-curl.log"

# Build WASM module if WASM mode is requested and module does not exist
WASM_MODULE="$ROOT/artifacts/wasm/sekibanwasm.wasm"
SEKIBAN_PROJECTION_RUNTIME="${SEKIBAN_PROJECTION_RUNTIME:-native}"

if [[ "$SEKIBAN_PROJECTION_RUNTIME" == "wasm" ]] && [[ ! -f "$WASM_MODULE" ]]; then
  echo "[e2e] WASM mode requested but module not found. Building..."
  "$ROOT/scripts/build-wasm.sh"
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
if [[ "$SEKIBAN_PROJECTION_RUNTIME" == "wasm" ]]; then
  echo "[e2e] Projection runtime: WASM"
  APPHOST_ENV+=("SEKIBAN_PROJECTION_RUNTIME=wasm")
  APPHOST_ENV+=("WASM_MODULE_PATH=$WASM_MODULE")
else
  echo "[e2e] Projection runtime: native"
fi

echo "[e2e] Starting Aspire AppHost: $APPHOST_PROJ"
(
  cd "$ROOT"
  env "${APPHOST_ENV[@]}" dotnet run --project "$APPHOST_PROJ"
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
