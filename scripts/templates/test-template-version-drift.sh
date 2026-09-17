#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

checker="scripts/templates/check-template-version-drift.py"
python3 "$checker"

fixture_dir="$(mktemp -d)"
trap 'rm -rf "$fixture_dir"' EXIT

# Keep a small, isolated copy so this regression proves a wrong template pin is
# rejected rather than only proving that the current tree is internally tidy.
mkdir -p \
  "$fixture_dir/templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/SekibanDcbDecider.Domain" \
  "$fixture_dir/templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/SekibanDcbDecider.AppHost" \
  "$fixture_dir/templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/.template.config" \
  "$fixture_dir/.github/workflows" \
  "$fixture_dir/docs/nuget"
cp templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/SekibanDcbDecider.Domain/SekibanDcbDecider.Domain.csproj \
  "$fixture_dir/templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/SekibanDcbDecider.Domain/"
cp templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj \
  "$fixture_dir/templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/SekibanDcbDecider.AppHost/"
cp templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/.template.config/template.json \
  "$fixture_dir/templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/.template.config/"
cp .github/workflows/release-templates-preview.yml "$fixture_dir/.github/workflows/"
cp README.md docs/public-packages.md docs/quickstart.md \
  "$fixture_dir/"
cp docs/nuget/package-readme.md docs/nuget/aspire-package-readme.md docs/nuget/templates-package-readme.md \
  "$fixture_dir/docs/nuget/"

python3 - "$fixture_dir/templates/Sekiban.Dcb.WasmRuntime.Templates/content/Sekiban.Dcb.WasmRuntime.Aspire.Decider/SekibanDcbDecider.Domain/SekibanDcbDecider.Domain.csproj" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
path.write_text(
    path.read_text(encoding="utf-8").replace('Version="10.22.0"', 'Version="10.18.0"'),
    encoding="utf-8",
)
PY

if output=$(python3 "$checker" --root "$fixture_dir" 2>&1); then
  printf '%s\n' "$output" >&2
  echo "expected the wrong template pin fixture to fail" >&2
  exit 1
fi
printf '%s\n' "$output" | grep -F "generated Domain Sekiban.Dcb.WithoutResult" >/dev/null
echo "PASS: template version drift fixture rejected the wrong Domain pin."
