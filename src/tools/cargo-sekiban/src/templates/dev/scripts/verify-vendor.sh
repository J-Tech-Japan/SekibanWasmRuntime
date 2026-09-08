#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

required=(sekiban-core sekiban-derive sekiban-wasm sekiban-mv sekiban-executor domain)
for crate in "${required[@]}"; do
  [[ -s "vendor/$crate/Cargo.toml" ]] || {
    echo "missing vendored crate: vendor/$crate" >&2
    exit 1
  }
done

manifests=(Cargo.toml Client/Cargo.toml Wasm/Cargo.toml)
for crate in "${required[@]}"; do manifests+=("vendor/$crate/Cargo.toml"); done
manifest_matches() {
  local pattern="$1"
  if command -v rg >/dev/null 2>&1; then
    rg -n "$pattern" "${manifests[@]}"
  else
    grep -En "$pattern" "${manifests[@]}"
  fi
}

matches=""
status=0
matches="$(manifest_matches 'wasm-projectors/rust|path[[:space:]]*=[[:space:]]*"/(Users|home|private)/')" || status=$?
if [[ "$status" -gt 1 ]]; then
  echo "could not scan generated dev manifests" >&2
  exit 1
fi
if [[ "$status" -eq 0 && -n "$matches" ]]; then
  echo "generated dev workspace still contains an original-checkout or absolute path" >&2
  printf '%s\n' "$matches" >&2
  exit 1
fi

cargo metadata --manifest-path Cargo.toml --format-version 1 >/dev/null
cargo check --manifest-path Cargo.toml --workspace
echo "dev vendor guard passed: the workspace is self-contained"
