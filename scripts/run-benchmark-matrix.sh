#!/usr/bin/env bash
# Orchestrates the projection-mode benchmark matrix: each runtime is executed in two
# projection modes (memory-only + materialized-view-only) and the peak-RSS / throughput
# numbers land under benchmarks/results/matrix/. Between runs we tear down any lingering
# Aspire child processes + Docker containers because concurrent AppHosts would fight over
# the shared ports the individual AppHosts hard-code.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
total_events="${TOTAL_EVENTS:-100000}"
results_dir="${RESULTS_DIR:-$repo_root/benchmarks/results/matrix}"
mkdir -p "$results_dir"

matrix=(
  "cs-wasm"
  "rs-wasm"
  "mb-wasm"
  "go-wasm"
  "ts-wasm"
  "swift-wasm"
  "native"
)
modes=(
  "memory-only"
  "materialized-view-only"
)

cleanup_background_state() {
  # Kill any stragglers from a prior run: AppHost dotnet processes, their wasmserver /
  # clientapi descendants, the sampler loop, and every Postgres / Azurite container Aspire
  # might have spawned. Tolerate errors because "no match" is the happy path. Additionally,
  # force-close every listener on the AppHost ports — language-specific clientapi executables
  # (go-clientapi, ts-clientapi node, swift run, cargo run) don't all match one name pattern,
  # and "Port already in use" is the #1 cause of cascade failures in the matrix.
  pkill -f 'SekibanDcbDecider|wasmserver|Sekiban.Dcb.WasmRuntime.Host|SekibanDcbDecider.AppHost' 2>/dev/null || true
  pkill -f 'run-benchmark-runtime.sh' 2>/dev/null || true
  pkill -f 'go-clientapi|ts-clientapi|Hummingbird|clientapi/src/server' 2>/dev/null || true
  sleep 2
  local pids
  pids="$(lsof -ti tcp:5141,5198,5199,6198,6199,6298,6299,7198,7199,7208,7209 -sTCP:LISTEN 2>/dev/null || true)"
  if [[ -n "$pids" ]]; then
    echo "  Killing leftover listeners: $pids"
    echo "$pids" | xargs -I {} kill -9 {} 2>/dev/null || true
    sleep 2
  fi
  local containers
  containers="$(docker ps --format '{{.Names}}' 2>/dev/null | grep -Ei 'postgres|azurite|sekiban' || true)"
  if [[ -n "$containers" ]]; then
    echo "  Stopping Docker containers: $containers"
    echo "$containers" | xargs -I {} docker stop {} >/dev/null 2>&1 || true
  fi
}

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
manifest="$results_dir/matrix-$timestamp.txt"
echo "# Sekiban benchmark matrix $timestamp (total-events=$total_events)" >"$manifest"
echo "# runtime,mode,output-json,rss-log,peak-rss-mb" >>"$manifest"

export SKIP_CS_WASM_MODULE_REBUILD=1
export SKIP_RS_WASM_MODULE_REBUILD=1
export SKIP_GO_WASM_MODULE_REBUILD=1
export SKIP_TS_WASM_MODULE_REBUILD=1
export SKIP_SWIFT_WASM_MODULE_REBUILD=1

for runtime in "${matrix[@]}"; do
  for mode in "${modes[@]}"; do
    label="${runtime}-${mode}-${timestamp}"
    output_json="$results_dir/${label}.json"
    rss_log="$results_dir/${label}.rss"
    echo ""
    echo "============================================================"
    echo "[matrix] ${runtime} × ${mode}"
    echo "============================================================"
    cleanup_background_state
    set +e
    bash "$repo_root/scripts/run-benchmark-runtime.sh" \
      --mode "$mode" \
      "$runtime" "$total_events" "$output_json" "$rss_log"
    status=$?
    set -e
    peak_rss="(n/a)"
    if [[ -f "$rss_log" ]]; then
      peak_rss="$(awk 'BEGIN { max = 0 } { if ($2 + 0 > max) max = $2 + 0 } END { printf "%.2f", max }' "$rss_log")"
    fi
    printf "%s,%s,%s,%s,%s,status=%d\n" \
      "$runtime" "$mode" "$output_json" "$rss_log" "$peak_rss" "$status" >>"$manifest"
    echo "[matrix] ${runtime}/${mode}: peak RSS=${peak_rss} MB, exit=${status}"
  done
done

cleanup_background_state
echo ""
echo "[matrix] complete. manifest: $manifest"
