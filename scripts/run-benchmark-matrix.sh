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

# Container name prefixes Aspire gives the AppHost-spawned resources. Anything matching
# one of these prefixes (plus the Aspire-appended random suffix) was started by this PR's
# AppHosts; everything else on the Docker daemon is left alone.
APPHOST_CONTAINER_PREFIXES=(
  'dcbOrleansPostgres-'
  'SekibanCSharpDb-'
  'sekibanRustPostgres-'
  'sekibanMoonBitPostgres-'
  'sekibanGoPostgres-'
  'sekibanTsPostgres-'
  'sekibanSwiftPostgres-'
  'azurestorage-'
  'dbgate-'
)
# Ports our AppHosts hard-code for the wasmserver / clientapi / native apiservice. Kept
# in sync with scripts/run-benchmark-runtime.sh.
APPHOST_PORTS='5141,5198,5199,6198,6199,6298,6299,7198,7199,7208,7209'

cleanup_background_state() {
  # Stop the AppHost-spawned processes by name pattern first. Matched patterns cover the
  # AppHost's child processes (wasmserver, apiservice) and the language-specific
  # clientapi executables (go-clientapi, ts-clientapi, Hummingbird on Swift, cargo on Rust)
  # — broad enough to catch leftovers from a failed cell.
  pkill -f 'SekibanDcbDecider|wasmserver|Sekiban.Dcb.WasmRuntime.Host|SekibanDcbDecider.AppHost' 2>/dev/null || true
  pkill -f 'run-benchmark-runtime.sh' 2>/dev/null || true
  pkill -f 'go-clientapi|ts-clientapi|Hummingbird|clientapi/src/server' 2>/dev/null || true
  sleep 2

  # Close listeners on the AppHost ports — only if the listener belongs to the current
  # user AND its command line matches a known benchmark/AppHost pattern. This protects
  # unrelated services that happen to use the same ports. Escalate to SIGKILL when the
  # operator explicitly opts in via RUN_BENCHMARK_MATRIX_FORCE_KILL_LISTENERS=1 for
  # stubborn leftovers; the default SIGTERM path is enough in practice.
  local pids current_user signal
  current_user="$(id -u)"
  if [[ "${RUN_BENCHMARK_MATRIX_FORCE_KILL_LISTENERS:-0}" == "1" ]]; then
    signal="-9"
  else
    signal="-TERM"
  fi
  pids="$(lsof -ti tcp:${APPHOST_PORTS} -sTCP:LISTEN 2>/dev/null || true)"
  if [[ -n "$pids" ]]; then
    local target_pids=()
    local pid
    for pid in $pids; do
      [[ -z "$pid" ]] && continue
      local uid cmd
      uid="$(ps -o uid= -p "$pid" 2>/dev/null | awk '{print $1}')"
      cmd="$(ps -o command= -p "$pid" 2>/dev/null || true)"
      if [[ "$uid" == "$current_user" ]] \
         && [[ "$cmd" =~ SekibanDcbDecider|wasmserver|Sekiban\.Dcb\.WasmRuntime\.Host|go-clientapi|ts-clientapi|Hummingbird|clientapi/src/server|cargo ]]; then
        target_pids+=("$pid")
      fi
    done
    if (( ${#target_pids[@]} > 0 )); then
      echo "  Signaling ($signal) leftover AppHost listeners: ${target_pids[*]}"
      kill "$signal" "${target_pids[@]}" 2>/dev/null || true
      sleep 2
    fi
  fi

  # Stop Aspire-spawned Docker containers matching the AppHost resource prefixes above.
  # Containers not matching the prefix list are untouched so unrelated databases the
  # developer / CI host is running don't get caught in the sweep.
  local all_containers containers=()
  all_containers="$(docker ps --format '{{.Names}}' 2>/dev/null || true)"
  if [[ -n "$all_containers" ]]; then
    while IFS= read -r name; do
      [[ -z "$name" ]] && continue
      local prefix
      for prefix in "${APPHOST_CONTAINER_PREFIXES[@]}"; do
        if [[ "$name" == "$prefix"* ]]; then
          containers+=("$name")
          break
        fi
      done
    done <<<"$all_containers"
  fi
  if (( ${#containers[@]} > 0 )); then
    echo "  Stopping Aspire-spawned containers: ${containers[*]}"
    docker stop "${containers[@]}" >/dev/null 2>&1 || true
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
