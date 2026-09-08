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
if rg -n 'wasm-projectors/rust|path\s*=\s*"/(Users|home|private)/' "${manifests[@]}"; then
  echo "generated dev workspace still contains an original-checkout or absolute path" >&2
  exit 1
fi

cargo metadata --manifest-path Cargo.toml --format-version 1 >/dev/null
cargo check --manifest-path Cargo.toml --workspace
echo "dev vendor guard passed: the workspace is self-contained"
