#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

fail=0

require_file() {
  local path="$1"
  if [[ ! -s "$path" ]]; then
    printf 'FAIL: required release file is missing or empty: %s\n' "$path" >&2
    fail=1
  else
    printf 'ok: %s\n' "$path"
  fi
}

require_text() {
  local path="$1"
  local pattern="$2"
  local description="$3"
  if ! grep -Eq "$pattern" "$path"; then
    printf 'FAIL: %s missing in %s\n' "$description" "$path" >&2
    fail=1
  else
    printf 'ok: %s\n' "$description"
  fi
}

require_file LICENSE
require_file NOTICE
require_file README.md
require_file docs/nuget/package-readme.md
require_file reports/public-release/readiness-inventory.md
require_file reports/public-release/hygiene-guardrail.md
require_file reports/public-release/wasmtime-preview-inspection.md
require_file reports/public-release/consumer-smoke-local-packages.md
require_file reports/public-release/release-artifact-provenance-sbom-readiness.md

require_text Directory.Build.props '<PackageLicenseFile>LICENSE</PackageLicenseFile>' 'NuGet packages declare LICENSE'
require_text Directory.Build.props '<PackageReadmeFile>README.md</PackageReadmeFile>' 'NuGet packages declare README'
require_text Directory.Build.props '<RepositoryUrl>https://github.com/J-Tech-Japan/SekibanWasmRuntime</RepositoryUrl>' 'NuGet packages declare repository URL'
require_text README.md 'Elastic License 2\.0|Elastic License' 'README license disclosure'
require_text NOTICE 'Wasmtime|Sekiban' 'NOTICE attribution content'
require_text reports/public-release/release-artifact-provenance-sbom-readiness.md 'Formal SBOM and provenance attestations are deferred' 'preview SBOM/provenance deferral'

if (( fail != 0 )); then
  exit 1
fi

printf 'license and notice check passed\n'
