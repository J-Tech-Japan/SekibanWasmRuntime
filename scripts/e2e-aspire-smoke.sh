#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

APPHOST_PROJ="$ROOT/src/internalUsage/SekibanWasm.AppHost/SekibanWasm.AppHost.csproj"
READY_URL_PATH="${READY_URL_PATH:-/api/weatherforecast}"
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

E2E_API_PORT="${E2E_API_PORT:-$(pick_free_port)}"
API_BASE_URL="${API_BASE_URL:-http://127.0.0.1:${E2E_API_PORT}}"

TS="$(date +%Y%m%d-%H%M%S)"
ARTIFACT_DIR="$ROOT/artifacts/e2e/$TS"
mkdir -p "$ARTIFACT_DIR"

APPHOST_LOG="$ARTIFACT_DIR/apphost.log"
API_HEALTH_LOG="$ARTIFACT_DIR/api-curl.log"

cleanup() {
  if [[ -n "${APPHOST_PID:-}" ]] && kill -0 "$APPHOST_PID" 2>/dev/null; then
    kill "$APPHOST_PID" 2>/dev/null || true
    sleep 2 || true
    kill -9 "$APPHOST_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

echo "[e2e] Starting Aspire AppHost: $APPHOST_PROJ"
(
  cd "$ROOT"
  E2E_API_PORT="$E2E_API_PORT" dotnet run --project "$APPHOST_PROJ"
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

echo "[e2e] OK (artifacts: $ARTIFACT_DIR)"
