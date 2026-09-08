#!/usr/bin/env bash
# SWR-G091 standalone generation proof.
#
# Generate both cargo-sekiban modes outside this checkout, then validate the
# dependency boundary, Cargo workspace, WASM fixture shape, and the generated
# channel-owned smoke. Runtime execution may be SKIP only for an unavailable
# Docker/.NET/WASM toolchain; a live smoke failure is a real failure.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLI_MANIFEST="$ROOT/src/tools/cargo-sekiban/Cargo.toml"
WORK_DIR="$(mktemp -d /private/tmp/cargo-sekiban-generated.XXXXXX)"
TASK_CARGO_HOME="$WORK_DIR/cargo-home"
export CARGO_HOME="$TASK_CARGO_HOME"

cleanup() {
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
  rg -q '^- Result: \*\*(PASS|SKIP)\*\*$' "$report" \
    || fail "generated smoke report has no PASS/SKIP result: $report"
  if rg -q '^- Result: \*\*SKIP\*\*$' "$report"; then
    log "runtime smoke recorded SKIP: $report"
  else
    log "runtime smoke recorded PASS: $report"
  fi
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
