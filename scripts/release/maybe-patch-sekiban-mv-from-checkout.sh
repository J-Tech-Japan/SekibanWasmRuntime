#!/usr/bin/env bash
# Until sekiban-mv 0.1.1 is on crates.io, monorepo CI can patch a generated
# registry workspace to resolve the fixed crate from a vendored copy inside that
# workspace (relative path only — never an absolute checkout path).
set -euo pipefail

WORKSPACE="${1:-}"
if [[ -z "$WORKSPACE" || ! -f "$WORKSPACE/Cargo.toml" ]]; then
  echo "usage: $0 <registry-workspace-root>" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MV_CRATE="$ROOT/src/wasm-projectors/rust/sekiban-mv"
RUST_WORKSPACE="$ROOT/src/wasm-projectors/rust/Cargo.toml"
VERSION="0.1.1"
VENDOR_REL="vendor/sekiban-mv"
VENDOR_DIR="$WORKSPACE/$VENDOR_REL"

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

echo "[maybe-patch-sekiban-mv] vendoring sekiban-mv ${VERSION} into ${VENDOR_REL} (not yet on crates.io)"
rm -rf "$VENDOR_DIR"
mkdir -p "$(dirname "$VENDOR_DIR")"
cp -a "$MV_CRATE/." "$VENDOR_DIR/"

python3 - "$VENDOR_DIR/Cargo.toml" "$MV_CRATE/Cargo.toml" "$RUST_WORKSPACE" <<'PY'
import re
import sys
from pathlib import Path

dest, source_manifest, workspace_manifest = map(Path, sys.argv[1:4])
text = source_manifest.read_text(encoding="utf-8")
workspace = workspace_manifest.read_text(encoding="utf-8")

package_block = re.search(r"\[package\][\s\S]*?(?=\n\[|$)", text)
if package_block is None:
    raise SystemExit("sekiban-mv [package] section missing")

lines = []
for raw in package_block.group(0).splitlines():
    line = raw.rstrip()
    if re.search(r"\.workspace\s*=\s*true", line):
        key = line.split(".", 1)[0].strip()
        wp = re.search(
            rf"^\s*{re.escape(key)}\s*=\s*(.+?)\s*$",
            workspace,
            flags=re.MULTILINE,
        )
        if wp is None:
            raise SystemExit(f"workspace.package.{key} missing")
        lines.append(f"{key} = {wp.group(1)}")
        continue
    lines.append(line)

body = "\n".join(lines).rstrip() + "\n\n"
body += "[dependencies]\n"
body += 'sekiban-wasm = "=0.1.0"\n'
for dep_line in text.splitlines():
    stripped = dep_line.strip()
    if stripped.startswith("sekiban-wasm"):
        continue
    if stripped.startswith("serde") or stripped.startswith("serde_json") or stripped.startswith("uuid"):
        body += dep_line.strip() + "\n"

dest.write_text(body, encoding="utf-8")
PY

cat >> "$WORKSPACE/Cargo.toml" <<PATCH

[patch.crates-io]
sekiban-mv = { path = "${VENDOR_REL}" }
PATCH
