#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

E2E_SAMPLE="${E2E_SAMPLE:-cs}"
case "$E2E_SAMPLE" in
  cs)
    STACK_ID="cs-apphost"
    APPHOST_PROJ="$ROOT/src/internalUsages/cs/SekibanWasm.Cs.AppHost/SekibanWasm.Cs.AppHost.csproj"
    BUILD_WASM_SCRIPT="$ROOT/build/scripts/build-csharp-wasm.sh"
    WASM_MODULE="$ROOT/src/internalUsages/cs/modules/csharp-weather.wasm"
    LANGUAGE_SAMPLE="cs"
    APPHOST_KIND="generic"
    ;;
  rust)
    STACK_ID="rust-apphost"
    APPHOST_PROJ="$ROOT/src/internalUsages/rust/SekibanWasm.Rust.AppHost/SekibanWasm.Rust.AppHost.csproj"
    BUILD_WASM_SCRIPT="$ROOT/build/scripts/build-rust-wasm.sh"
    WASM_MODULE="$ROOT/src/internalUsages/rust/modules/rust-weather.wasm"
    LANGUAGE_SAMPLE="rust"
    APPHOST_KIND="generic"
    ;;
  cs-generic)
    STACK_ID="cs-generic"
    APPHOST_PROJ="$ROOT/src/internalUsages/cs/SekibanWasm.Cs.GenericAppHost/SekibanWasm.Cs.GenericAppHost.csproj"
    BUILD_WASM_SCRIPT="$ROOT/build/scripts/build-csharp-wasm.sh"
    WASM_MODULE="$ROOT/src/internalUsages/cs/modules/csharp-weather.wasm"
    LANGUAGE_SAMPLE="cs"
    APPHOST_KIND="generic"
    ;;
  rust-generic)
    STACK_ID="rust-generic"
    APPHOST_PROJ="$ROOT/src/internalUsages/rust/SekibanWasm.Rust.GenericAppHost/SekibanWasm.Rust.GenericAppHost.csproj"
    BUILD_WASM_SCRIPT="$ROOT/build/scripts/build-rust-wasm.sh"
    WASM_MODULE="$ROOT/src/internalUsages/rust/modules/rust-weather.wasm"
    LANGUAGE_SAMPLE="rust"
    APPHOST_KIND="generic"
    ;;
  *)
    echo "[e2e-playwright] ERROR: unknown E2E_SAMPLE='$E2E_SAMPLE' (expected: cs|rust|cs-generic|rust-generic)" >&2
    exit 2
    ;;
esac

pick_free_port() {
  node - <<'NODE'
import net from 'net';
const server = net.createServer();
server.listen(0, '127.0.0.1', () => {
  const { port } = server.address();
  server.close(() => console.log(String(port)));
});
NODE
}

E2E_WEB_PORT="${E2E_WEB_PORT:-$(pick_free_port)}"
E2E_API_PORT="${E2E_API_PORT:-$(pick_free_port)}"
E2E_CLIENT_API_PORT="${E2E_CLIENT_API_PORT:-$(pick_free_port)}"
E2E_POSTGRES_PORT="${E2E_POSTGRES_PORT:-$(pick_free_port)}"
E2E_DBGATE_PORT="${E2E_DBGATE_PORT:-$(pick_free_port)}"
WASM_API_BASE_URL="${WASM_API_BASE_URL:-http://127.0.0.1:${E2E_API_PORT}}"
CLIENT_API_BASE_URL="${CLIENT_API_BASE_URL:-http://127.0.0.1:${E2E_CLIENT_API_PORT}}"
WEB_BASE_URL="${WEB_BASE_URL:-http://127.0.0.1:${E2E_WEB_PORT}}"
APPHOST_PORT="${APPHOST_PORT:-$(pick_free_port)}"
RESOURCE_SERVICE_PORT="${RESOURCE_SERVICE_PORT:-$(pick_free_port)}"
OTLP_PORT="${OTLP_PORT:-$(pick_free_port)}"

TS="$(date +%Y%m%d-%H%M%S)"
ARTIFACT_DIR="$ROOT/artifacts/e2e-playwright/$STACK_ID/$TS"
mkdir -p "$ARTIFACT_DIR"

APPHOST_LOG="$ARTIFACT_DIR/apphost.log"

if [[ ! -f "$WASM_MODULE" ]]; then
  echo "[e2e-playwright] WASM module not found. Building via: $BUILD_WASM_SCRIPT"
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

dump_apphost_tail() {
  echo "[e2e-playwright] Last AppHost log lines:"
  tail -n 200 "$APPHOST_LOG" | tee "$ARTIFACT_DIR/apphost-tail.txt" || true
}

json_field() {
  local json="$1"
  local expression="$2"
  node -e '
const payload = JSON.parse(process.argv[1]);
const expr = process.argv[2];
let value = payload;
for (const part of expr.split(".")) {
  value = value?.[part];
}
if (value === undefined || value === null) {
  process.exit(1);
}
if (typeof value === "object") {
  process.stdout.write(JSON.stringify(value));
} else {
  process.stdout.write(String(value));
}
' "$json" "$expression"
}

forecast_location_from_list() {
  local json="$1"
  local forecast_id="$2"
  node -e '
const items = JSON.parse(process.argv[1]);
const forecastId = process.argv[2];
const item = Array.isArray(items) ? items.find((entry) => entry.forecastId === forecastId) : undefined;
if (!item) process.exit(1);
process.stdout.write(String(item.location ?? ""));
' "$json" "$forecast_id"
}

forecast_exists_in_list() {
  local json="$1"
  local forecast_id="$2"
  node -e '
const items = JSON.parse(process.argv[1]);
const forecastId = process.argv[2];
process.exit(Array.isArray(items) && items.some((entry) => entry.forecastId === forecastId) ? 0 : 1);
' "$json" "$forecast_id"
}

wait_for_forecast_location() {
  local forecast_id="$1"
  local expected_location="$2"
  local sortable_unique_id="$3"
  local body_file
  body_file="$(mktemp)"

  for attempt in $(seq 1 30); do
    local http_code
    http_code="$(curl --max-time 10 -s -o "$body_file" -w '%{http_code}' \
      "${CLIENT_API_BASE_URL}/api/weatherforecast?waitForSortableId=${sortable_unique_id}" || true)"

    if [[ "$http_code" == "200" ]]; then
      local location=""
      location="$(forecast_location_from_list "$(cat "$body_file")" "$forecast_id" 2>/dev/null || true)"
      if [[ "$location" == "$expected_location" ]]; then
        rm -f "$body_file"
        return 0
      fi
    fi

    sleep 2
  done

  echo "[e2e-playwright] Forecast did not become visible in time for $forecast_id"
  cat "$body_file"
  echo
  rm -f "$body_file"
  return 1
}

wait_for_forecast_absent() {
  local forecast_id="$1"
  local sortable_unique_id="$2"
  local body_file
  body_file="$(mktemp)"

  for attempt in $(seq 1 30); do
    local http_code
    http_code="$(curl --max-time 10 -s -o "$body_file" -w '%{http_code}' \
      "${CLIENT_API_BASE_URL}/api/weatherforecast?waitForSortableId=${sortable_unique_id}" || true)"

    if [[ "$http_code" == "200" ]] && ! forecast_exists_in_list "$(cat "$body_file")" "$forecast_id"; then
      rm -f "$body_file"
      return 0
    fi

    sleep 2
  done

  echo "[e2e-playwright] Forecast did not disappear in time for $forecast_id"
  cat "$body_file"
  echo
  rm -f "$body_file"
  return 1
}

warm_client_api() {
  local unique forecast_id location payload body_file http_code delete_payload create_body delete_body create_sortable delete_sortable
  unique="$(date +%s%N)"
  forecast_id="00000000-0000-0000-0000-${unique: -12}"
  location="warmup-${LANGUAGE_SAMPLE}-${unique}"
  payload="$(printf '{"forecastId":"%s","location":"%s","temperatureC":21,"summary":"Warmup"}' "$forecast_id" "$location")"
  body_file="$(mktemp)"

  for attempt in $(seq 1 20); do
    http_code="$(curl --max-time 5 -s -o "$body_file" -w '%{http_code}' \
      -H 'Content-Type: application/json' \
      -d "$payload" \
      "${CLIENT_API_BASE_URL}/api/weatherforecast" || true)"

    if [[ "$http_code" == "200" ]] && grep -q '"success":true' "$body_file"; then
      create_body="$(cat "$body_file")"
      create_sortable="$(json_field "$create_body" "sortableUniqueId" 2>/dev/null || true)"
      if [[ -n "$create_sortable" ]]; then
        wait_for_forecast_location "$forecast_id" "$location" "$create_sortable" || {
          dump_apphost_tail
          rm -f "$body_file"
          exit 1
        }
      fi

      delete_payload="$(printf '{"forecastId":"%s"}' "$forecast_id")"
      delete_body="$(curl --max-time 10 -s \
        -H 'Content-Type: application/json' \
        -d "$delete_payload" \
        "${CLIENT_API_BASE_URL}/api/weatherforecast/delete" || true)"
      delete_sortable="$(json_field "$delete_body" "sortableUniqueId" 2>/dev/null || true)"
      if [[ -n "$delete_sortable" ]]; then
        wait_for_forecast_absent "$forecast_id" "$delete_sortable" || {
          dump_apphost_tail
          rm -f "$body_file"
          exit 1
        }
      fi
      rm -f "$body_file"
      return 0
    fi

    if ! kill -0 "$APPHOST_PID" 2>/dev/null; then
      echo "[e2e-playwright] AppHost exited during ClientApi warmup. See: $APPHOST_LOG"
      dump_apphost_tail
      rm -f "$body_file"
      exit 1
    fi

    sleep 2
  done

  echo "[e2e-playwright] ClientApi warmup failed after repeated create attempts. Last response:"
  cat "$body_file"
  echo
  dump_apphost_tail
  rm -f "$body_file"
  exit 1
}

APPHOST_ENV=()
case "$STACK_ID" in
  cs-apphost|rust-apphost)
    APPHOST_ENV+=("E2E_WEB_PORT=$E2E_WEB_PORT")
    APPHOST_ENV+=("E2E_API_PORT=$E2E_API_PORT")
    APPHOST_ENV+=("E2E_CLIENT_API_PORT=$E2E_CLIENT_API_PORT")
    ;;
  cs-generic)
    APPHOST_ENV+=("CS_GENERIC_WEB_PORT=$E2E_WEB_PORT")
    APPHOST_ENV+=("CS_GENERIC_RUNTIME_PORT=$E2E_API_PORT")
    APPHOST_ENV+=("CS_GENERIC_CLIENT_API_PORT=$E2E_CLIENT_API_PORT")
    APPHOST_ENV+=("CS_GENERIC_POSTGRES_PORT=$E2E_POSTGRES_PORT")
    APPHOST_ENV+=("CS_GENERIC_DBGATE_PORT=$E2E_DBGATE_PORT")
    ;;
  rust-generic)
    APPHOST_ENV+=("RUST_GENERIC_WEB_PORT=$E2E_WEB_PORT")
    APPHOST_ENV+=("RUST_GENERIC_RUNTIME_PORT=$E2E_API_PORT")
    APPHOST_ENV+=("RUST_GENERIC_CLIENT_API_PORT=$E2E_CLIENT_API_PORT")
    APPHOST_ENV+=("RUST_GENERIC_POSTGRES_PORT=$E2E_POSTGRES_PORT")
    APPHOST_ENV+=("RUST_GENERIC_DBGATE_PORT=$E2E_DBGATE_PORT")
    ;;
esac
APPHOST_ENV+=("ASPNETCORE_URLS=http://127.0.0.1:${APPHOST_PORT}")
APPHOST_ENV+=("DOTNET_RESOURCE_SERVICE_ENDPOINT_URL=http://127.0.0.1:${RESOURCE_SERVICE_PORT}")
APPHOST_ENV+=("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL=http://127.0.0.1:${OTLP_PORT}")
APPHOST_ENV+=("ASPIRE_ALLOW_UNSECURED_TRANSPORT=true")
APPHOST_ENV+=("SEKIBAN_PROJECTION_RUNTIME=wasm")
APPHOST_ENV+=("WASM_MODULE_PATH=$WASM_MODULE")

PLAYWRIGHT_SPECS=(
  "tests/weather-clientapi-crud.spec.js"
  "tests/weather-web-ui-crud.spec.js"
)

if [[ "$STACK_ID" == "cs-apphost" ]]; then
  PLAYWRIGHT_SPECS+=("tests/weather-crud.spec.js")
fi

echo "[e2e-playwright] Starting Aspire AppHost: $APPHOST_PROJ"
(
  cd "$ROOT"
  env "${APPHOST_ENV[@]}" dotnet run --no-launch-profile --project "$APPHOST_PROJ"
) >"$APPHOST_LOG" 2>&1 &
APPHOST_PID="$!"

echo "[e2e-playwright] Waiting for WasmServer API: ${WASM_API_BASE_URL}/openapi/v1.json"
deadline=$((SECONDS + 300))
until curl -fsS --max-time 2 "${WASM_API_BASE_URL}/openapi/v1.json" >/dev/null 2>&1; do
  if ! kill -0 "$APPHOST_PID" 2>/dev/null; then
    echo "[e2e-playwright] AppHost exited before API became ready. See: $APPHOST_LOG"
    tail -n 200 "$APPHOST_LOG" >"$ARTIFACT_DIR/apphost-tail.txt" || true
    exit 1
  fi
  if (( SECONDS > deadline )); then
    echo "[e2e-playwright] Timeout waiting for API. See: $APPHOST_LOG"
    tail -n 200 "$APPHOST_LOG" >"$ARTIFACT_DIR/apphost-tail.txt" || true
    exit 1
  fi
  sleep 2
done

echo "[e2e-playwright] Waiting for ClientApi: ${CLIENT_API_BASE_URL}/health"
deadline=$((SECONDS + 180))
until curl -fsS --max-time 2 "${CLIENT_API_BASE_URL}/health" >/dev/null 2>&1; do
  if ! kill -0 "$APPHOST_PID" 2>/dev/null; then
    echo "[e2e-playwright] AppHost exited before ClientApi became ready. See: $APPHOST_LOG"
    tail -n 200 "$APPHOST_LOG" >"$ARTIFACT_DIR/apphost-tail.txt" || true
    exit 1
  fi
  if (( SECONDS > deadline )); then
    echo "[e2e-playwright] Timeout waiting for ClientApi. See: $APPHOST_LOG"
    tail -n 200 "$APPHOST_LOG" >"$ARTIFACT_DIR/apphost-tail.txt" || true
    exit 1
  fi
  sleep 2
done

echo "[e2e-playwright] Waiting for Web UI: ${WEB_BASE_URL}/"
deadline=$((SECONDS + 180))
until curl -fsS --max-time 2 "${WEB_BASE_URL}/" >/dev/null 2>&1; do
  if ! kill -0 "$APPHOST_PID" 2>/dev/null; then
    echo "[e2e-playwright] AppHost exited before Web UI became ready. See: $APPHOST_LOG"
    tail -n 200 "$APPHOST_LOG" >"$ARTIFACT_DIR/apphost-tail.txt" || true
    exit 1
  fi
  if (( SECONDS > deadline )); then
    echo "[e2e-playwright] Timeout waiting for Web UI. See: $APPHOST_LOG"
    tail -n 200 "$APPHOST_LOG" >"$ARTIFACT_DIR/apphost-tail.txt" || true
    exit 1
  fi
  sleep 2
done

echo "[e2e-playwright] Warming up ClientApi command path..."
warm_client_api

echo "[e2e-playwright] Running Playwright (headless)..."
(
  cd "$ROOT/e2e/playwright"
  npm install
  if [[ "$(uname -s)" == "Linux" ]]; then
    npx playwright install --with-deps chromium
  else
    npx playwright install chromium
  fi
  WEB_BASE_URL="$WEB_BASE_URL" \
  WASM_API_BASE_URL="$WASM_API_BASE_URL" \
  CLIENT_API_BASE_URL="$CLIENT_API_BASE_URL" \
  E2E_SAMPLE="$LANGUAGE_SAMPLE" \
  E2E_APPHOST_KIND="$APPHOST_KIND" \
  npx playwright test "${PLAYWRIGHT_SPECS[@]}" --project=chromium --output "$ARTIFACT_DIR/test-results"
) || {
  dump_apphost_tail
  exit 1
}

echo "[e2e-playwright] OK (artifacts: $ARTIFACT_DIR)"
