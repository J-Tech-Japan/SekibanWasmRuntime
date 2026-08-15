#!/usr/bin/env bash
#
# Run the portable suite against the current runtime host in a disposable
# Postgres-backed container, including a real process restart between phases.

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
compose_dir="$repo_root/docker/sekiban-wasm-runtime"
compose_file="$compose_dir/docker-compose.yml"
module_source="${SWR_G082_WASM_MODULE:-$repo_root/src/internalUsages/cs/modules/csharp-weather.wasm}"
module_destination="$compose_dir/modules/weather.wasm"
report_path="${SWR_G082_REPORT:-$repo_root/reports/compatibility/serialized-dcb-v1-conformance.md}"
project_name="sekiban-dcb-v1-$$"
copied_module=0

fail() {
  printf '[dcb-v1-ci] FAIL: %s\n' "$*" >&2
  exit 1
}

pick_port() {
  python3 -c 'import socket
sock = socket.socket()
sock.bind(("127.0.0.1", 0))
print(sock.getsockname()[1])
sock.close()'
}

command -v docker >/dev/null 2>&1 || fail "docker is required"
docker info >/dev/null 2>&1 || fail "docker daemon is unavailable"
[[ -f "$compose_file" ]] || fail "compose file not found: $compose_file"
[[ -s "$module_source" ]] || fail "WASM module not found: $module_source"

runtime_port="${SEKIBAN_RUNTIME_PORT:-$(pick_port)}"
postgres_port="${SEKIBAN_POSTGRES_PORT:-$(pick_port)}"
dbgate_port="${SEKIBAN_DBGATE_PORT:-$(pick_port)}"
export SEKIBAN_RUNTIME_PORT="$runtime_port"
export SEKIBAN_POSTGRES_PORT="$postgres_port"
export SEKIBAN_DBGATE_PORT="$dbgate_port"
runtime_url="http://127.0.0.1:${runtime_port}"
runtime_image=""
runtime_container="sekiban-dcb-v1-$$-runtime"
runtime_started=0

compose() {
  docker compose -f "$compose_file" -p "$project_name" "$@"
}

cleanup() {
  if ((runtime_started == 1)); then
    docker rm -f "$runtime_container" >/dev/null 2>&1 || true
  fi
  compose down -v --remove-orphans >/dev/null 2>&1 || true
  if ((copied_module == 1)); then
    rm -f "$module_destination"
  fi
}
trap cleanup EXIT

if [[ ! -s "$module_destination" ]]; then
  cp "$module_source" "$module_destination"
  copied_module=1
fi

compose build runtime
compose up -d postgres
runtime_image="${project_name}-runtime:latest"
docker image inspect "$runtime_image" >/dev/null 2>&1 || fail "built runtime image not found: $runtime_image"
runtime_started=1
docker run -d \
  --name "$runtime_container" \
  --network "${project_name}_default" \
  -p "${runtime_port}:8080" \
  -e ASPNETCORE_URLS=http://0.0.0.0:8080 \
  -e "ConnectionStrings__SekibanDcb=Host=postgres;Port=5432;Database=sekiban;Username=postgres;Password=postgres" \
  -e SEKIBAN_MANIFEST_PATH=/app/config/sekiban-manifest.json \
  -e WASM_MODULE_PATH=/app/modules/weather.wasm \
  -e SEKIBAN_SERVICE_ID=swr-g082-conformance \
  -e SEKIBAN_PROJECTION_MODE=memory-only \
  -v "${compose_dir}/config:/app/config:ro" \
  -v "${compose_dir}/modules:/app/modules:ro" \
  "$runtime_image" >/dev/null

deadline=$((SECONDS + 180))
ready=0
while ((SECONDS < deadline)); do
  if curl -q --silent --show-error --fail --max-time 5 "$runtime_url/ready" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 3
done
((ready == 1)) || {
  docker logs --tail 200 "$runtime_container" >&2 || true
  fail "runtime did not become ready: $runtime_url/ready"
}

root_response="$(curl -q --silent --show-error --max-time 10 "$runtime_url/" || true)"
printf '%s' "$root_response" | grep -q 'Sekiban WASM Runtime Host' \
  || fail "target identity check failed: $root_response"

printf '[dcb-v1-ci] running external suite against %s\n' "$runtime_url"
bash "$repo_root/scripts/contract/run-serialized-dcb-v1.sh" \
  --base-url "$runtime_url" \
  --restart-command "docker restart $runtime_container" \
  --report "$report_path"

printf '[dcb-v1-ci] PASS report=%s\n' "$report_path"
