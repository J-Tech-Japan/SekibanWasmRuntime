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

case "$runtime" in
  native)
    apphost_project="$repo_root/submodules/Sekiban/templates/Sekiban.Dcb.Templates/content/Sekiban.Dcb.Orleans.Decider/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:5141"
    ready_port="5141"
    rss_port="5141"
    mode_label="native-300k-$(date -u +%Y%m%d-%H%M%S)"
    ;;
  cs-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:5198"
    ready_port="5198"
    rss_port="5199"
    mode_label="cs-wasm-300k-$(date -u +%Y%m%d-%H%M%S)"
    ;;
  rs-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:6198"
    ready_port="6198"
    rss_port="6199"
    mode_label="rs-wasm-300k-$(date -u +%Y%m%d-%H%M%S)"
    ;;
  mb-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:6198"
    ready_port="6198"
    rss_port="6199"
    mode_label="mb-wasm-300k-$(date -u +%Y%m%d-%H%M%S)"
    ;;
  *)
    echo "Unknown runtime: $runtime" >&2
    exit 1
    ;;
esac

apphost_pid=""
sampler_pid=""
runtime_pid=""

cleanup() {
  set +e
  if [[ -n "$sampler_pid" ]] && kill -0 "$sampler_pid" 2>/dev/null; then
    kill "$sampler_pid" 2>/dev/null || true
    wait "$sampler_pid" 2>/dev/null || true
  fi

  if [[ -n "$apphost_pid" ]] && kill -0 "$apphost_pid" 2>/dev/null; then
    kill "$apphost_pid" 2>/dev/null || true
    wait "$apphost_pid" 2>/dev/null || true
  fi

  if [[ -n "$runtime_pid" ]] && kill -0 "$runtime_pid" 2>/dev/null; then
    kill "$runtime_pid" 2>/dev/null || true
  fi

  if lsof -ti tcp:"$ready_port" >/dev/null 2>&1; then
    lsof -ti tcp:"$ready_port" | xargs kill -TERM 2>/dev/null || true
  fi

  if [[ "$rss_port" != "$ready_port" ]] && lsof -ti tcp:"$rss_port" >/dev/null 2>&1; then
    lsof -ti tcp:"$rss_port" | xargs kill -TERM 2>/dev/null || true
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
    sleep "$delay"
  done

  echo "Timed out waiting for port $port" >&2
  return 1
}

echo "[$runtime] starting AppHost: $apphost_project"
rm -f "$apphost_log" "$benchmark_log" "$rss_log"
dotnet run --project "$apphost_project" >"$apphost_log" 2>&1 &
apphost_pid="$!"

wait_for_port "$ready_port"
wait_for_port "$rss_port"

runtime_pid="$(lsof -ti tcp:"$rss_port" -sTCP:LISTEN | head -n 1)"
if [[ -z "$runtime_pid" ]]; then
  echo "Failed to locate runtime pid on port $rss_port" >&2
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
dotnet run --project "$repo_root/benchmarks/Sekiban.Benchmark.Cli/Sekiban.Benchmark.Cli.csproj" -- \
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
