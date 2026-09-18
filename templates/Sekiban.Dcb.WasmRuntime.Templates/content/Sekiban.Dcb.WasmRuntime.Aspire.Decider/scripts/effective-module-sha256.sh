#!/usr/bin/env bash
# Hash the WASM bytes Wasmtime actually instantiates. Core modules hash directly;
# component artifacts require wasm-tools to extract the embedded core module.
set -euo pipefail

effective_module_sha256() {
  local module_path="$1"
  local header
  header="$(od -An -tx1 -N8 "$module_path" | tr -d ' \n')"
  if [[ "$header" != "0061736d0d000100" ]]; then
    sha256sum "$module_path" | awk '{print $1}'
    return 0
  fi

  command -v wasm-tools >/dev/null 2>&1 || {
    echo "[effective-module-sha256] ERROR: wasm-tools is required to hash the instantiated core module" >&2
    return 1
  }

  local extract_dir
  extract_dir="$(mktemp -d "${TMPDIR:-/tmp}/swr-mv-component.XXXXXX")"
  local module_dir="$extract_dir/modules"
  mkdir -p "$module_dir"
  if ! wasm-tools component unbundle "$module_path" \
      --module-dir "$module_dir" -o "$extract_dir/component.wasm" >/dev/null; then
    rm -rf "$extract_dir"
    echo "[effective-module-sha256] ERROR: could not extract the instantiated core module" >&2
    return 1
  fi

  local core_module_count=0
  local core_module_path=""
  while IFS= read -r candidate; do
    core_module_count=$((core_module_count + 1))
    core_module_path="$candidate"
  done < <(find "$module_dir" -maxdepth 1 -name 'unbundled-module*.wasm' -type f | sort)
  if [[ "$core_module_count" -ne 1 ]]; then
    rm -rf "$extract_dir"
    echo "[effective-module-sha256] ERROR: expected exactly one embedded core module, found $core_module_count" >&2
    return 1
  fi

  local digest
  digest="$(sha256sum "$core_module_path" | awk '{print $1}')"
  rm -rf "$extract_dir"
  printf '%s' "$digest"
}
