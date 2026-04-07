#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 ]]; then
  cat <<'EOF' >&2
Usage:
  scripts/run-benchmark-runtime.sh <runtime> <total-events> <output-json> <rss-log>

Runtime:
  native   - Sekiban template native C#
  cs-wasm  - C# WASM sample
  rs-wasm  - Rust WASM sample
  mb-wasm  - MoonBit WASM sample
  go-wasm  - Go WASM sample
EOF
  exit 1
fi

runtime="$1"
total_events="$2"
output_json="$3"
rss_log="$4"

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$(dirname "$output_json")" "$(dirname "$rss_log")"

apphost_log="${output_json%.json}.apphost.log"
benchmark_log="${output_json%.json}.benchmark.log"
run_timestamp="$(date -u +%Y%m%d-%H%M%S)"

if [[ "$total_events" =~ ^[0-9]+$ ]] && (( total_events % 1000 == 0 )); then
  events_label="$((total_events / 1000))k"
else
  events_label="$total_events"
fi

case "$runtime" in
  native)
    apphost_project="$repo_root/submodules/Sekiban/templates/Sekiban.Dcb.Templates/content/Sekiban.Dcb.Orleans.Decider/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:5141"
    ready_port="5141"
    rss_port="5141"
    runtime_process_pattern='SekibanDcbDecider\.ApiService'
    mode_label="native-${events_label}-${run_timestamp}"
    ;;
  cs-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:5198"
    ready_port="5198"
    rss_port="5199"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="cs-wasm-${events_label}-${run_timestamp}"
    ;;
  rs-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:6198"
    ready_port="6198"
    rss_port="6199"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="rs-wasm-${events_label}-${run_timestamp}"
    ;;
  mb-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:6198"
    ready_port="6198"
    rss_port="6199"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="mb-wasm-${events_label}-${run_timestamp}"
    ;;
  go-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Go/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:7198"
    ready_port="7198"
    rss_port="7199"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="go-wasm-${events_label}-${run_timestamp}"
    ;;
  *)
    echo "Unknown runtime: $runtime" >&2
    exit 1
    ;;
esac

apphost_pid=""
sampler_pid=""
runtime_pid=""

terminate_pid_tree() {
  local pid="$1"
  local child

  if [[ -z "$pid" ]] || ! kill -0 "$pid" 2>/dev/null; then
    return 0
  fi

  while IFS= read -r child; do
    [[ -n "$child" ]] || continue
    terminate_pid_tree "$child"
  done < <(pgrep -P "$pid" 2>/dev/null || true)

  kill -TERM "$pid" 2>/dev/null || true
}

cleanup() {
  set +e
  terminate_pid_tree "$sampler_pid"
  if [[ -n "$sampler_pid" ]]; then
    wait "$sampler_pid" 2>/dev/null || true
  fi

  terminate_pid_tree "$runtime_pid"
  if [[ -n "$runtime_pid" ]]; then
    wait "$runtime_pid" 2>/dev/null || true
  fi

  terminate_pid_tree "$apphost_pid"
  if [[ -n "$apphost_pid" ]]; then
    wait "$apphost_pid" 2>/dev/null || true
  fi
}

trap cleanup EXIT

wait_for_port() {
  local port="$1"
  local retries="${2:-180}"
  local delay="${3:-2}"
  local attempt

  for ((attempt = 1; attempt <= retries; attempt++)); do
    if nc -z 127.0.0.1 "$port" >/dev/null 2>&1; then
      return 0
    fi

    if [[ -n "$apphost_pid" ]] && ! kill -0 "$apphost_pid" 2>/dev/null; then
      echo "AppHost exited before opening port $port. Tail of $apphost_log:" >&2
      tail -n 40 "$apphost_log" >&2 || true
      return 1
    fi

    sleep "$delay"
  done

  echo "Timed out waiting for port $port" >&2
  tail -n 40 "$apphost_log" >&2 || true
  return 1
}

assert_port_free() {
  local port="$1"

  if lsof -ti tcp:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Port $port is already in use before startup" >&2
    return 1
  fi
}

is_descendant_pid() {
  local parent_pid="$1"
  local target_pid="$2"
  local child_pid

  if [[ "$parent_pid" == "$target_pid" ]]; then
    return 0
  fi

  while IFS= read -r child_pid; do
    [[ -z "$child_pid" ]] && continue
    if [[ "$child_pid" == "$target_pid" ]]; then
      return 0
    fi
    if is_descendant_pid "$child_pid" "$target_pid"; then
      return 0
    fi
  done < <(pgrep -P "$parent_pid" 2>/dev/null || true)

  return 1
}

find_listening_pid_for_port_under_parent() {
  local port="$1"
  local parent_pid="$2"
  local candidate_pid

  while IFS= read -r candidate_pid; do
    [[ -z "$candidate_pid" ]] && continue
    if is_descendant_pid "$parent_pid" "$candidate_pid"; then
      printf '%s\n' "$candidate_pid"
      return 0
    fi
  done < <(lsof -ti tcp:"$port" -sTCP:LISTEN | awk '!seen[$0]++')

  return 1
}

find_listening_pid_for_port_matching_pattern() {
  local port="$1"
  local pattern="$2"
  local candidate_pid
  local command_line

  while IFS= read -r candidate_pid; do
    [[ -z "$candidate_pid" ]] && continue
    command_line="$(ps -o command= -p "$candidate_pid" 2>/dev/null || true)"
    if [[ -n "$command_line" ]] && [[ "$command_line" =~ $pattern ]]; then
      printf '%s\n' "$candidate_pid"
      return 0
    fi
  done < <(lsof -ti tcp:"$port" -sTCP:LISTEN | awk '!seen[$0]++')

  return 1
}

if [[ ! -f "$apphost_project" ]]; then
  echo "AppHost project not found: $apphost_project" >&2
  echo "If this is a fresh clone, run: git submodule update --init --recursive" >&2
  exit 1
fi

echo "[$runtime] starting AppHost: $apphost_project"
rm -f "$apphost_log" "$benchmark_log" "$rss_log"
assert_port_free "$ready_port"
if [[ "$rss_port" != "$ready_port" ]]; then
  assert_port_free "$rss_port"
fi
dotnet run --project "$apphost_project" -c Release >"$apphost_log" 2>&1 &
apphost_pid="$!"

wait_for_port "$ready_port"
wait_for_port "$rss_port"

runtime_pid="$(find_listening_pid_for_port_under_parent "$rss_port" "$apphost_pid" || true)"
if [[ -z "$runtime_pid" ]]; then
  runtime_pid="$(find_listening_pid_for_port_matching_pattern "$rss_port" "$runtime_process_pattern" || true)"
fi
if [[ -z "$runtime_pid" ]]; then
  echo "Failed to locate runtime pid on port $rss_port under AppHost pid $apphost_pid" >&2
  tail -n 40 "$apphost_log" >&2 || true
  exit 1
fi

echo "[$runtime] sampling RSS from pid $runtime_pid on port $rss_port -> $rss_log"
(
  while kill -0 "$runtime_pid" 2>/dev/null; do
    rss_kb="$(ps -o rss= -p "$runtime_pid" | awk 'NR==1 {gsub(/^[[:space:]]+/, "", $1); print $1}')"
    if [[ -n "$rss_kb" ]]; then
      rss_mb="$(awk -v kb="$rss_kb" 'BEGIN { printf "%.2f", kb / 1024 }')"
      printf "%s %s\n" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$rss_mb"
    fi
    sleep 2
  done
) >"$rss_log" &
sampler_pid="$!"

echo "[$runtime] running benchmark against $base_url"
dotnet run --project "$repo_root/benchmarks/Sekiban.Benchmark.Cli/Sekiban.Benchmark.Cli.csproj" -c Release -- \
  --base-url "$base_url" \
  --mode-label "$mode_label" \
  --total-events "$total_events" \
  --concurrency 8 \
  --output "$output_json" | tee "$benchmark_log"

if kill -0 "$sampler_pid" 2>/dev/null; then
  kill "$sampler_pid" 2>/dev/null || true
  wait "$sampler_pid" 2>/dev/null || true
fi

peak_rss="$(awk 'BEGIN { max = 0 } { if ($2 + 0 > max) max = $2 + 0 } END { printf "%.2f", max }' "$rss_log")"
echo "[$runtime] peak RSS: ${peak_rss} MB"
echo "[$runtime] results: $output_json"
echo "[$runtime] logs: $benchmark_log / $apphost_log / $rss_log"
