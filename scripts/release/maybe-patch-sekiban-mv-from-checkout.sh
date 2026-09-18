#!/usr/bin/env bash
# Until sekiban-mv 0.1.1 is on crates.io, monorepo CI can patch a generated
# registry workspace to resolve the fixed crate from this checkout.
set -euo pipefail

WORKSPACE="${1:-}"
if [[ -z "$WORKSPACE" || ! -f "$WORKSPACE/Cargo.toml" ]]; then
  echo "usage: $0 <registry-workspace-root>" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MV_CRATE="$ROOT/src/wasm-projectors/rust/sekiban-mv"
VERSION="0.1.1"

if [[ ! -f "$MV_CRATE/Cargo.toml" ]]; then
  echo "sekiban-mv checkout crate missing at $MV_CRATE" >&2
  exit 1
fi

code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 \
  -H 'User-Agent: sekiban-wasm-runtime-generation-check (+https://github.com/J-Tech-Japan/SekibanWasmRuntime)' \
  "https://crates.io/api/v1/crates/sekiban-mv/${VERSION}" || echo "000")"
if [[ "$code" == "200" ]]; then
  echo "[maybe-patch-sekiban-mv] sekiban-mv ${VERSION} is on crates.io; no patch needed"
  exit 0
fi

if grep -q '^\[patch\.crates-io\]' "$WORKSPACE/Cargo.toml"; then
  echo "[maybe-patch-sekiban-mv] workspace already has [patch.crates-io]; leaving unchanged"
  exit 0
fi

echo "[maybe-patch-sekiban-mv] patching sekiban-mv ${VERSION} from checkout (not yet on crates.io)"
cat >> "$WORKSPACE/Cargo.toml" <<PATCH

[patch.crates-io]
sekiban-mv = { path = "${MV_CRATE}" }
PATCH
