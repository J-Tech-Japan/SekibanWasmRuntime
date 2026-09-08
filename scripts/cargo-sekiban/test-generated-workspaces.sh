#!/usr/bin/env bash
# SWR-G091 standalone generation proof.
#
# Generate both cargo-sekiban modes outside this checkout, then validate the
# dependency boundary, Cargo workspace, WASM fixture shape, and the generated
# channel-owned smoke. Runtime execution may be SKIP only for an unavailable
# Docker/.NET/WASM toolchain; a live smoke failure is a real failure.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLI_MANIFEST="$ROOT/src/tools/cargo-sekiban/Cargo.toml"
TMP_ROOT="${TMPDIR:-/tmp}"
WORK_DIR=""
if ! WORK_DIR="$(mktemp -d "${TMP_ROOT%/}/cargo-sekiban-generated.XXXXXX")"; then
  printf '[cargo-sekiban-generation] FAIL: mktemp could not create the standalone proof directory under %s\n' "$TMP_ROOT" >&2
  exit 1
fi
if [[ -z "$WORK_DIR" || ! -d "$WORK_DIR" ]]; then
  printf '[cargo-sekiban-generation] FAIL: mktemp returned an unusable standalone proof directory: %s\n' "$WORK_DIR" >&2
  exit 1
fi
TASK_CARGO_HOME="$WORK_DIR/cargo-home"
export CARGO_HOME="$TASK_CARGO_HOME"
TASK_DOTNET_HOME="$WORK_DIR/dotnet-home"
export DOTNET_CLI_HOME="$TASK_DOTNET_HOME"
TASK_NUGET_PACKAGES="$WORK_DIR/nuget-packages"
export NUGET_PACKAGES="$TASK_NUGET_PACKAGES"
TASK_NUGET_HTTP_CACHE="$WORK_DIR/nuget-http-cache"
export NUGET_HTTP_CACHE_PATH="$TASK_NUGET_HTTP_CACHE"
REPORT_OUTPUT_DIR="${CARGO_SEKIBAN_REPORT_DIR:-}"

cleanup() {
  if [[ -n "$REPORT_OUTPUT_DIR" && -d "$WORK_DIR" ]]; then
    mkdir -p "$REPORT_OUTPUT_DIR"
    for mode in registry dev; do
      if [[ -d "$WORK_DIR/$mode/reports" ]]; then
        mkdir -p "$REPORT_OUTPUT_DIR/$mode"
        cp -R "$WORK_DIR/$mode/reports/." "$REPORT_OUTPUT_DIR/$mode/" \
          || log "WARN: could not retain $mode smoke reports in $REPORT_OUTPUT_DIR"
      fi
    done
  fi
  rm -rf "$WORK_DIR"
}
trap cleanup EXIT

log() { printf '[cargo-sekiban-generation] %s\n' "$*"; }
fail() { log "FAIL: $*"; exit 1; }

log "checking deterministic embedded template source sync"
python3 "$ROOT/scripts/cargo-sekiban/sync-templates.py" --check \
  || fail "template assets are not synchronized"

generate() {
  local name="$1" mode="$2" output="$3"
  log "generating $mode project at $output"
  cargo run --quiet --manifest-path "$CLI_MANIFEST" -- \
    new "$name" --template decider --mode "$mode" --path "$output" \
    || fail "cargo-sekiban generation failed for $mode"
}

assert_no_checkout_residue() {
  local output="$1"
  python3 - "$output" "$ROOT" <<'PY'
import sys
from pathlib import Path

output = Path(sys.argv[1])
checkout = sys.argv[2]
bad = []
for path in output.rglob("*"):
    if not path.is_file() or any(part in {"target", "artifacts", "reports"} for part in path.parts):
        continue
    try:
        text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        continue
    if checkout in text or "src/samples/" in text or "src/wasm-projectors/rust/" in text:
        bad.append(str(path.relative_to(output)))
if bad:
    print("checkout/path residue: " + ", ".join(bad), file=sys.stderr)
    raise SystemExit(1)
PY
}

assert_fixture_shape() {
  local output="$1"
  rg -q 'WeatherForecastCreated|WeatherForecastLocationUpdated|WeatherForecastProjector|weather_forecast' \
    "$output/scripts/build-wasm.sh" \
    || fail "$output is missing the serialized weather fixture manifest shape"
  rg -q 'RemoteSekibanExecutor|execute_command|get_state|execute_list_query|execute_query' \
    "$output/Client/src/main.rs" \
    || fail "$output is missing the typed command/tag/query proof"
  rg -q '/health|/ready|cargo run' "$output/scripts/smoke.sh" \
    || fail "$output is missing the channel-owned health/client smoke"
}

assert_registry_contract() {
  local output="$1"
  python3 - "$output" <<'PY'
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])
sekiban = ("sekiban-core", "sekiban-derive", "sekiban-wasm", "sekiban-mv", "sekiban-executor")
manifests = sorted(root.glob("*/Cargo.toml"))
text = "\n".join(path.read_text(encoding="utf-8") for path in manifests)
for crate in sekiban:
    if not re.search(rf"^\s*{re.escape(crate)}\s*=\s*\"=0\.1\.0\"\s*$", text, re.MULTILINE):
        raise SystemExit(f"{crate} is not pinned to exact =0.1.0")
    if re.search(rf"^\s*{re.escape(crate)}\s*=.*path\s*=", text, re.MULTILINE):
        raise SystemExit(f"{crate} has a local path dependency")
PY
  (cd "$output" && bash scripts/verify-no-local-sekiban-paths.sh) \
    || fail "registry no-local-path guard failed"
}

assert_dev_contract() {
  local output="$1"
  (cd "$output" && bash scripts/verify-vendor.sh) \
    || fail "dev vendor guard failed"
}

build_wasm_if_available() {
  local output="$1"
  local target_libdir=""
  if command -v rustc >/dev/null 2>&1; then
    target_libdir="$(rustc --target wasm32-wasip1 --print target-libdir 2>/dev/null || true)"
  fi
  if [[ -n "$target_libdir" ]] && compgen -G "$target_libdir/libcore-*.rlib" >/dev/null; then
    log "building generated WASM module in $output"
    (cd "$output" && bash scripts/build-wasm.sh) \
      || fail "generated WASM build failed for $output"
  elif [[ "${CARGO_SEKIBAN_REQUIRE_WASM:-0}" == "1" ]]; then
    fail "wasm32-wasip1 target is required for generated WASM validation"
  else
    log "SKIP generated WASM build for $output: active rustc cannot use wasm32-wasip1"
  fi
}

run_smoke() {
  local output="$1" slug="$2"
  local report="$output/reports/smoke/$slug-smoke.md"
  log "running generated channel-owned smoke for $output"
  (cd "$output" && bash scripts/smoke.sh) \
    || fail "generated runtime smoke failed for $output"
  [[ -s "$report" ]] || fail "generated smoke did not write $report"
  local result detail
  result="$(sed -nE 's/^- Result: \*\*(PASS|SKIP|FAIL)\*\*$/\1/p' "$report")"
  detail="$(sed -nE 's/^- Detail: (.*)$/\1/p' "$report")"
  case "$result" in
    PASS)
      log "runtime smoke recorded PASS: $report"
      ;;
    SKIP)
      case "$detail" in
        "Docker is not available."|"dotnet SDK not found."|"cargo not found."|"active rustc cannot use the wasm32-wasip1 target."|"could not build the Rust WASM module; install the wasm32-wasip1 target and re-run.")
          if [[ "${CARGO_SEKIBAN_REQUIRE_RUNTIME:-0}" == "1" && "$detail" != "Docker is not available." ]]; then
            fail "generated smoke recorded a non-Docker SKIP in strict runtime mode: $detail"
          fi
          if [[ "${CARGO_SEKIBAN_REQUIRE_WASM:-0}" == "1" && "$detail" == *wasm32-wasip1* ]]; then
            fail "generated smoke recorded a WASI-target SKIP in strict WASM mode: $detail"
          fi
          log "runtime smoke recorded sanctioned SKIP ($detail): $report"
          ;;
        *)
          fail "generated smoke recorded an unsanctioned SKIP reason: $detail"
          ;;
      esac
      ;;
    FAIL)
      fail "generated smoke report recorded FAIL: $detail"
      ;;
    *)
      fail "generated smoke report has no single PASS/SKIP/FAIL result: $report"
      ;;
  esac
  log "generated smoke report:"
  sed -n '1,240p' "$report"
}

REGISTRY="$WORK_DIR/registry"
DEV="$WORK_DIR/dev"
generate weather-registry registry "$REGISTRY"
generate weather-dev dev "$DEV"

for output in "$REGISTRY" "$DEV"; do
  assert_no_checkout_residue "$output" \
    || fail "generated output contains checkout residue: $output"
  assert_fixture_shape "$output" \
    || fail "generated output is missing fixture proof: $output"
done

assert_registry_contract "$REGISTRY"
assert_dev_contract "$DEV"
build_wasm_if_available "$REGISTRY"
build_wasm_if_available "$DEV"
run_smoke "$REGISTRY" weather-registry
run_smoke "$DEV" weather-dev

log "PASS: both generated workspaces are standalone and their smoke reports are explicit"
