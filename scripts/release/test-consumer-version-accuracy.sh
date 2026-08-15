#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

fixture_dir="$(mktemp -d)"
trap 'rm -rf "$fixture_dir"' EXIT

fixture="$fixture_dir/current.md"
cp docs/public-packages.md "$fixture"

python3 scripts/release/check-consumer-version-accuracy.py \
  --package-version 1.0.0-preview.5 \
  --dcb-version 10.14.0 \
  --runtime-image-version 1.0.0-preview.3 \
  --document "$fixture" \
  --skip-package-artifact >/dev/null

python3 - "$fixture" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text(encoding="utf-8")
text = text.replace("`1.0.0-preview.5`. <!-- release-lane: current-package-version -->", "`1.0.0-preview.4`. <!-- release-lane: current-package-version -->", 1)
path.write_text(text, encoding="utf-8")
PY

if output=$(python3 scripts/release/check-consumer-version-accuracy.py \
  --package-version 1.0.0-preview.5 \
  --dcb-version 10.14.0 \
  --runtime-image-version 1.0.0-preview.3 \
  --document "$fixture" \
  --skip-package-artifact 2>&1); then
  echo "expected the wrong-version fixture to fail" >&2
  exit 1
fi

printf '%s\n' "$output" | grep -F "$fixture" >/dev/null
echo "Consumer version accuracy fixture tests passed"
