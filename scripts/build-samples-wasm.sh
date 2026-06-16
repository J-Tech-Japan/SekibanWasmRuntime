#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'EOF'
Usage: scripts/build-samples-wasm.sh [--primary|--sample csharp|--sample rust]

Builds generated sample WASM modules into src/samples/**/modules/.

Support tiers:
  primary:
    csharp  src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/modules/sekiban-dcb-decider.wasm
    rust    src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/modules/sekiban-dcb-decider-rust.wasm

  experimental/reference:
    go, moonbit, typescript, swift

The default is --primary. Generated .wasm outputs are intentionally ignored by git.
EOF
}

samples=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --primary)
      samples=(csharp rust)
      shift
      ;;
    --sample)
      if [[ $# -lt 2 ]]; then
        echo "[build-samples-wasm] ERROR: --sample requires a value" >&2
        usage >&2
        exit 2
      fi
      samples+=("$2")
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "[build-samples-wasm] ERROR: unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ ${#samples[@]} -eq 0 ]]; then
  samples=(csharp rust)
fi

build_csharp() {
  echo "[build-samples-wasm] Building primary C# sample WASM"
  "$ROOT/build/scripts/build-sample-csharp-wasm.sh"
}

build_rust() {
  local sample_root="$ROOT/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs"
  local module_path="$sample_root/modules/sekiban-dcb-decider-rust.wasm"
  local wasm_file="$sample_root/target/wasm32-wasip1/release/sekiban_dcb_decider_rust_wasm.wasm"

  echo "[build-samples-wasm] Building primary Rust sample WASM"
  mkdir -p "$(dirname "$module_path")"
  cargo build \
    --manifest-path "$sample_root/Cargo.toml" \
    --package sekiban-dcb-decider-rust-wasm \
    --target wasm32-wasip1 \
    --release

  if [[ ! -f "$wasm_file" ]]; then
    echo "[build-samples-wasm] ERROR: Rust WASM output not found: $wasm_file" >&2
    exit 1
  fi

  cp "$wasm_file" "$module_path"
  echo "[build-samples-wasm] built: $module_path ($(wc -c < "$module_path") bytes)"
}

for sample in "${samples[@]}"; do
  case "$sample" in
    csharp|cs|cs-wasm)
      build_csharp
      ;;
    rust|rs|rs-wasm)
      build_rust
      ;;
    *)
      echo "[build-samples-wasm] ERROR: unsupported sample '$sample'. Primary samples are csharp and rust." >&2
      exit 2
      ;;
  esac
done

echo "[build-samples-wasm] completed"
