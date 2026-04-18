#!/usr/bin/env bash
set -euo pipefail

projection_mode="dual"
# Detached --mode <value> flag; scan before positional parsing so it can sit anywhere.
positional=()
while (( $# > 0 )); do
  case "$1" in
    --mode)
      projection_mode="$2"
      shift 2
      ;;
    --mode=*)
      projection_mode="${1#--mode=}"
      shift
      ;;
    *)
      positional+=("$1")
      shift
      ;;
  esac
done
set -- "${positional[@]}"

case "$projection_mode" in
  dual|memory-only|materialized-view-only)
    ;;
  *)
    echo "Unsupported --mode: $projection_mode (expected dual | memory-only | materialized-view-only)" >&2
    exit 1
    ;;
esac

if [[ $# -lt 4 ]]; then
  cat <<'EOF' >&2
Usage:
  scripts/run-benchmark-runtime.sh [--mode <mode>] <runtime> <total-events> <output-json> <rss-log> [benchmark-profile]

Runtime:
  native     - Sekiban template native C#
  cs-wasm    - C# WASM sample
  rs-wasm    - Rust WASM sample
  mb-wasm    - MoonBit WASM sample
  go-wasm    - Go WASM sample
  ts-wasm    - TypeScript WASM sample
  swift-wasm - Swift WASM sample

Modes (--mode, default=dual):
  dual                     - MultiProjection + MaterializedView both enabled (baseline)
  memory-only              - MaterializedView skipped; MultiProjection only
  materialized-view-only   - MultiProjection endpoints disabled; MaterializedView only
EOF
  exit 1
fi

runtime="$1"
total_events="$2"
output_json="$3"
rss_log="$4"
benchmark_profile="${5:-default}"

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

if [[ "$benchmark_profile" == "default" ]]; then
  profile_suffix=""
else
  profile_suffix="-$benchmark_profile"
fi

if [[ "$projection_mode" == "dual" ]]; then
  mode_suffix=""
else
  mode_suffix="-$projection_mode"
fi

case "$runtime" in
  native)
    apphost_project="$repo_root/submodules/Sekiban/templates/Sekiban.Dcb.Templates/content/Sekiban.Dcb.Orleans.Decider/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:5141"
    ready_port="5141"
    rss_port="5141"
    runtime_process_pattern='SekibanDcbDecider\.ApiService'
    mode_label="native-${events_label}${profile_suffix}${mode_suffix}-${run_timestamp}"
    ;;
  cs-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:5198"
    ready_port="5198"
    rss_port="5199"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="cs-wasm-${events_label}${profile_suffix}${mode_suffix}-${run_timestamp}"
    ;;
  rs-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:6198"
    ready_port="6198"
    rss_port="6199"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="rs-wasm-${events_label}${profile_suffix}${mode_suffix}-${run_timestamp}"
    ;;
  mb-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:6198"
    ready_port="6198"
    rss_port="6199"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="mb-wasm-${events_label}${profile_suffix}${mode_suffix}-${run_timestamp}"
    ;;
  go-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Go/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:7198"
    ready_port="7198"
    rss_port="7199"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="go-wasm-${events_label}${profile_suffix}${mode_suffix}-${run_timestamp}"
    ;;
  ts-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:7208"
    ready_port="7208"
    rss_port="7209"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="ts-wasm-${events_label}${profile_suffix}${mode_suffix}-${run_timestamp}"
    ;;
  swift-wasm)
    apphost_project="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Swift/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj"
    base_url="http://127.0.0.1:6298"
    ready_port="6298"
    rss_port="6299"
    runtime_process_pattern='Sekiban\.Dcb\.WasmRuntime\.Host|wasmserver'
    mode_label="swift-wasm-${events_label}${profile_suffix}${mode_suffix}-${run_timestamp}"
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

find_running_container_by_prefix() {
  local prefix="$1"

  docker ps --format '{{.Names}}' | awk -v prefix="$prefix" 'index($0, prefix) == 1 { print; exit }'
}

ensure_cs_wasm_sample_module() {
  local module_path="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/modules/sekiban-dcb-decider.wasm"
  local build_script="$repo_root/build/scripts/build-sample-csharp-wasm.sh"
  local newest_source=""

  if [[ "${SKIP_CS_WASM_MODULE_REBUILD:-0}" == "1" ]]; then
    return 0
  fi

  if [[ ! -f "$module_path" ]]; then
    echo "[cs-wasm] module missing, rebuilding sample C# WASM module"
    "$build_script"
    return 0
  fi

  newest_source="$(
    find \
      "$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.Wasm" \
      "$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.EventSource" \
      "$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.MeetingRoomModels" \
      "$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.ImmutableModels" \
      "$repo_root/submodules/Sekiban/dcb/src/Sekiban.Dcb.Core.Model" \
      "$repo_root/submodules/Sekiban/dcb/src/Sekiban.Dcb.WithoutResult.Model" \
      \( -path '*/obj/*' -o -path '*/bin/*' \) -prune -o \
      -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.json' \) -newer "$module_path" -print -quit
  )"

  if [[ -n "$newest_source" ]]; then
    echo "[cs-wasm] module is stale relative to $newest_source, rebuilding sample C# WASM module"
    "$build_script"
  fi
}

ensure_rs_wasm_sample_module() {
  local sample_root="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs"
  local module_path="$sample_root/modules/sekiban-dcb-decider-rust.wasm"
  local workspace_manifest="$sample_root/Cargo.toml"
  local newest_source=""

  if [[ "${SKIP_RS_WASM_MODULE_REBUILD:-0}" == "1" ]]; then
    return 0
  fi

  if [[ ! -f "$module_path" ]]; then
    echo "[rs-wasm] module missing, rebuilding sample Rust WASM module"
  else
    newest_source="$(
      find \
        "$sample_root/SekibanDcbDecider.Rust.EventSource" \
        "$sample_root/SekibanDcbDecider.Rust.Wasm" \
        "$repo_root/src/wasm-projectors/rust/sekiban-core" \
        "$repo_root/src/wasm-projectors/rust/sekiban-derive" \
        "$repo_root/src/wasm-projectors/rust/sekiban-wasm" \
        \( -path '*/target/*' -o -path '*/obj/*' -o -path '*/bin/*' \) -prune -o \
        -type f \( -name '*.rs' -o -name 'Cargo.toml' -o -name 'Cargo.lock' \) -newer "$module_path" -print -quit
    )"
  fi

  if [[ ! -f "$module_path" || -n "$newest_source" ]]; then
    if [[ -n "$newest_source" ]]; then
      echo "[rs-wasm] module is stale relative to $newest_source, rebuilding sample Rust WASM module"
    fi
    cargo build \
      --manifest-path "$workspace_manifest" \
      --package sekiban-dcb-decider-rust-wasm \
      --target wasm32-wasip1 \
      --release >/dev/null
    cp \
      "$sample_root/target/wasm32-wasip1/release/sekiban_dcb_decider_rust_wasm.wasm" \
      "$module_path"
  fi
}

ensure_ts_wasm_clientapi_dependencies() {
  local clientapi_dir="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/ts-clientapi"
  local marker="$clientapi_dir/node_modules/@hono/node-server/package.json"

  if [[ ! -f "$marker" ]]; then
    echo "[ts-wasm] ts-clientapi dependencies missing, running npm install"
    (cd "$clientapi_dir" && npm install --no-audit --no-fund)
  fi
}

ensure_go_wasm_sample_module() {
  local sample_root="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Go"
  local source_root="$sample_root/go-wasm"
  local module_path="$sample_root/modules/go-weather.wasm"
  local newest_source=""

  if [[ "${SKIP_GO_WASM_MODULE_REBUILD:-0}" == "1" ]]; then
    return 0
  fi

  if [[ ! -f "$module_path" ]]; then
    echo "[go-wasm] module missing, rebuilding sample Go WASM module"
  else
    newest_source="$(
      find \
        "$source_root" \
        \( -path '*/bin/*' -o -path '*/obj/*' \) -prune -o \
        -type f \( -name '*.go' -o -name 'go.mod' -o -name 'go.sum' -o -name '*.json' \) -newer "$module_path" -print -quit
    )"
  fi

  if [[ ! -f "$module_path" || -n "$newest_source" ]]; then
    if [[ -n "$newest_source" ]]; then
      echo "[go-wasm] module is stale relative to $newest_source, rebuilding sample Go WASM module"
    fi
    (
      cd "$source_root"
      tinygo build -target=wasi -buildmode=c-shared -o ../modules/go-weather.wasm ./wasm
    )
  fi
}

ensure_ts_wasm_sample_module() {
  local sample_root="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts"
  local module_dir="$sample_root/ts-wasm"
  local module_path="$sample_root/modules/ts-weather.wasm"
  local marker="$module_dir/node_modules/assemblyscript/package.json"
  local newest_source=""

  if [[ "${SKIP_TS_WASM_MODULE_REBUILD:-0}" == "1" ]]; then
    return 0
  fi

  if [[ ! -f "$marker" ]]; then
    echo "[ts-wasm] ts-wasm dependencies missing, running npm install"
    (cd "$module_dir" && npm install --no-audit --no-fund --legacy-peer-deps)
  fi

  if [[ ! -f "$module_path" ]]; then
    echo "[ts-wasm] module missing, rebuilding sample TypeScript WASM module"
  else
    newest_source="$(
      find \
        "$module_dir" \
        "$sample_root/modules/sekiban-runtime-manifest.json" \
        \( -path '*/node_modules/*' -o -path '*/dist/*' \) -prune -o \
        -type f \( -name '*.ts' -o -name '*.json' -o -name 'package-lock.json' -o -name 'package.json' \) -newer "$module_path" -print -quit
    )"
  fi

  if [[ ! -f "$module_path" || -n "$newest_source" ]]; then
    if [[ -n "$newest_source" ]]; then
      echo "[ts-wasm] module is stale relative to $newest_source, rebuilding sample TypeScript WASM module"
    fi
    (cd "$module_dir" && npm run build >/dev/null)
  fi
}

ensure_mb_wasm_clientapi_dependencies() {
  local clientapi_dir="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/SekibanDcbDecider.ClientApi"
  local marker="$clientapi_dir/node_modules/.package-lock.json"

  if [[ ! -f "$marker" ]]; then
    echo "[mb-wasm] ClientApi dependencies missing, running npm install"
    (cd "$clientapi_dir" && npm install --no-audit --no-fund >/dev/null)
  fi
}

ensure_swift_wasm_sample_module() {
  local sample_root="$repo_root/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Swift"
  local module_path="$sample_root/modules/sekiban-dcb-decider-swift.wasm"
  local build_script="$repo_root/build/scripts/build-swift-wasm.sh"

  if [[ "${SKIP_SWIFT_WASM_MODULE_REBUILD:-0}" == "1" ]]; then
    return 0
  fi

  if [[ ! -f "$module_path" ]]; then
    if [[ -x "$build_script" ]]; then
      echo "[swift-wasm] module missing, rebuilding Swift WASM module"
      "$build_script"
    else
      echo "[swift-wasm] module missing and build/scripts/build-swift-wasm.sh not executable; \
set SKIP_SWIFT_WASM_MODULE_REBUILD=1 and provide the .wasm manually." >&2
      return 1
    fi
  fi
}

ensure_postgres_database() {
  local container_name="$1"
  local password="$2"
  local database_name="$3"
  local exists

  exists="$(docker exec -e PGPASSWORD="$password" "$container_name" \
    psql -U postgres -h 127.0.0.1 -d postgres -tAc \
    "SELECT 1 FROM pg_database WHERE datname = '$database_name';" 2>/dev/null || true)"
  if [[ "$exists" == "1" ]]; then
    return 0
  fi

  if docker exec -e PGPASSWORD="$password" "$container_name" \
    psql -U postgres -h 127.0.0.1 -d postgres -c \
    "CREATE DATABASE \"$database_name\";" >/dev/null 2>&1; then
    return 0
  fi

  exists="$(docker exec -e PGPASSWORD="$password" "$container_name" \
    psql -U postgres -h 127.0.0.1 -d postgres -tAc \
    "SELECT 1 FROM pg_database WHERE datname = '$database_name';" 2>/dev/null || true)"
  [[ "$exists" == "1" ]]
}

bootstrap_native_postgres_databases() {
  local retries="${1:-60}"
  local delay="${2:-2}"
  local attempt
  local container_name
  local password

  for ((attempt = 1; attempt <= retries; attempt++)); do
    if [[ -n "$apphost_pid" ]] && ! kill -0 "$apphost_pid" 2>/dev/null; then
      echo "AppHost exited before PostgreSQL bootstrap completed" >&2
      return 1
    fi

    container_name="$(find_running_container_by_prefix "dcbOrleansPostgres-")"
    if [[ -z "$container_name" ]]; then
      sleep "$delay"
      continue
    fi

    password="$(docker inspect "$container_name" --format '{{range .Config.Env}}{{println .}}{{end}}' \
      | awk -F= '$1 == "POSTGRES_PASSWORD" { print $2; exit }')"
    if [[ -z "$password" ]]; then
      sleep "$delay"
      continue
    fi

    if ! docker exec -e PGPASSWORD="$password" "$container_name" \
      psql -U postgres -h 127.0.0.1 -d postgres -tAc 'SELECT 1;' >/dev/null 2>&1; then
      sleep "$delay"
      continue
    fi

    ensure_postgres_database "$container_name" "$password" "DcbPostgres"
    ensure_postgres_database "$container_name" "$password" "IdentityPostgres"
    return 0
  done

  echo "Timed out waiting for PostgreSQL database bootstrap" >&2
  return 1
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
echo "[$runtime] benchmark profile: $benchmark_profile"
rm -f "$apphost_log" "$benchmark_log" "$rss_log"
if [[ "$runtime" == "cs-wasm" ]]; then
  ensure_cs_wasm_sample_module
elif [[ "$runtime" == "rs-wasm" ]]; then
  ensure_rs_wasm_sample_module
elif [[ "$runtime" == "go-wasm" ]]; then
  ensure_go_wasm_sample_module
elif [[ "$runtime" == "mb-wasm" ]]; then
  ensure_mb_wasm_clientapi_dependencies
elif [[ "$runtime" == "ts-wasm" ]]; then
  ensure_ts_wasm_clientapi_dependencies
  ensure_ts_wasm_sample_module
elif [[ "$runtime" == "swift-wasm" ]]; then
  ensure_swift_wasm_sample_module
fi
assert_port_free "$ready_port"
if [[ "$rss_port" != "$ready_port" ]]; then
  assert_port_free "$rss_port"
fi
BENCHMARK_PROFILE="$benchmark_profile" \
SEKIBAN_PROJECTION_MODE="$projection_mode" \
dotnet run --project "$apphost_project" -c Release >"$apphost_log" 2>&1 &
apphost_pid="$!"

if [[ "$runtime" == "native" ]]; then
  bootstrap_native_postgres_databases
fi

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

echo "[$runtime] running benchmark against $base_url (projection mode: $projection_mode)"
benchmark_extra_args=()
# In materialized-view-only mode MultiProjection-backed query endpoints return 503, so the query
# phase would spend its entire budget on timeouts. Skip it — the write load still exercises the MV
# catch-up path which is the point of this mode.
if [[ "$projection_mode" == "materialized-view-only" ]]; then
  benchmark_extra_args+=(--skip-queries)
fi
dotnet run --project "$repo_root/benchmarks/Sekiban.Benchmark.Cli/Sekiban.Benchmark.Cli.csproj" -c Release -- \
  --base-url "$base_url" \
  --mode-label "$mode_label" \
  --total-events "$total_events" \
  --concurrency 8 \
  --output "$output_json" \
  ${benchmark_extra_args[@]+"${benchmark_extra_args[@]}"} | tee "$benchmark_log"

if kill -0 "$sampler_pid" 2>/dev/null; then
  kill "$sampler_pid" 2>/dev/null || true
  wait "$sampler_pid" 2>/dev/null || true
fi

peak_rss="$(awk 'BEGIN { max = 0 } { if ($2 + 0 > max) max = $2 + 0 } END { printf "%.2f", max }' "$rss_log")"
echo "[$runtime] peak RSS: ${peak_rss} MB"
echo "[$runtime] results: $output_json"
echo "[$runtime] logs: $benchmark_log / $apphost_log / $rss_log"
