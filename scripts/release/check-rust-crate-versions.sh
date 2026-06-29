#!/usr/bin/env bash
# Validate that every public Rust crate declares the expected synchronized
# version. Used by the recurring Rust crates release lane before any publish so
# a GitHub Release tag can only publish when the manifests already match.
#
# Usage: scripts/release/check-rust-crate-versions.sh <expected-version>
set -euo pipefail

EXPECTED="${1:?usage: check-rust-crate-versions.sh <expected-version>}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

CRATES=(sekiban-core sekiban-derive sekiban-wasm sekiban-mv sekiban-executor)
mismatch=0

for crate in "${CRATES[@]}"; do
  manifest="${ROOT}/src/wasm-projectors/rust/${crate}/Cargo.toml"
  version="$(cargo metadata --no-deps --format-version 1 --manifest-path "${manifest}" \
    | jq -r --arg crate "${crate}" '.packages[] | select(.name == $crate) | .version')"
  if [ "${version}" != "${EXPECTED}" ]; then
    echo "ERROR: ${crate} version ${version} does not match expected ${EXPECTED}" >&2
    mismatch=1
  else
    echo "ok: ${crate} ${version}"
  fi
done

if [ "${mismatch}" -ne 0 ]; then
  echo "ERROR: public Rust crate versions are not synchronized to ${EXPECTED}." >&2
  exit 1
fi

echo "All ${#CRATES[@]} public Rust crates are at ${EXPECTED}."
