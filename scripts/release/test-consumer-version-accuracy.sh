#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

fixture_dir="$(mktemp -d)"
trap 'rm -rf "$fixture_dir"' EXIT

fixture="$fixture_dir/current.md"
cp docs/public-packages.md "$fixture"

# This regression test proves the checker CAN fail; the real accuracy gate
# compares against the lane's PACKAGE_VERSION independently. Reading the
# marker value from the document itself is therefore correct here, and it
# removes the hardcoded current-line copy that rotted on every release.
package_version="$(grep -oE '\x60[0-9]+\.[0-9]+\.[0-9]+-preview\.[0-9]+\x60\. <!-- release-lane: current-package-version -->' docs/public-packages.md | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+-preview\.[0-9]+')"
mutated_version="${package_version%.*}.$(( ${package_version##*.} + 1 ))"
runtime_image_version="${RUNTIME_IMAGE_VERSION:-}"
if [[ -z "$runtime_image_version" ]]; then
  runtime_image_version="$(RUNTIME_IMAGE_TAG=preview scripts/release/resolve-runtime-host-image-version.sh)"
fi

python3 scripts/release/check-consumer-version-accuracy.py \
  --package-version "$package_version" \
  --dcb-version 10.16.0 \
  --runtime-image-version "$runtime_image_version" \
  --document "$fixture" \
  --skip-package-artifact >/dev/null

FIXTURE_CURRENT_VERSION="$package_version" FIXTURE_MUTATED_VERSION="$mutated_version" python3 - "$fixture" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text(encoding="utf-8")
import os
current = os.environ["FIXTURE_CURRENT_VERSION"]
mutated = os.environ["FIXTURE_MUTATED_VERSION"]
text = text.replace(f"`{current}`. <!-- release-lane: current-package-version -->", f"`{mutated}`. <!-- release-lane: current-package-version -->", 1)
path.write_text(text, encoding="utf-8")
PY

if output=$(python3 scripts/release/check-consumer-version-accuracy.py \
  --package-version "$package_version" \
  --dcb-version 10.16.0 \
  --runtime-image-version "$runtime_image_version" \
  --document "$fixture" \
  --skip-package-artifact 2>&1); then
  echo "expected the wrong-version fixture to fail" >&2
  exit 1
fi

printf '%s\n' "$output" | grep -F "$fixture" >/dev/null
echo "Consumer version accuracy fixture tests passed"
