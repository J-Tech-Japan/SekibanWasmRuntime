#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

nuget_dir="${NUGET_OUTPUT_DIR:-artifacts/nuget}"
report_dir="${RELEASE_REPORT_DIR:-artifacts/release}"
report_path="$report_dir/artifact-inventory.md"
mkdir -p "$report_dir"

{
  printf '# Release Artifact Inventory\n\n'
  printf 'Commit: `%s`\n\n' "$(git rev-parse HEAD)"
  printf '## NuGet Packages\n\n'
  if compgen -G "$nuget_dir/*.nupkg" >/dev/null; then
    printf '| File | Bytes | SHA256 |\n'
    printf '| --- | ---: | --- |\n'
    for package in "$nuget_dir"/*.nupkg; do
      bytes="$(wc -c < "$package" | tr -d ' ')"
      hash="$(shasum -a 256 "$package" | awk '{print $1}')"
      printf '| `%s` | %s | `%s` |\n' "$package" "$bytes" "$hash"
    done
  else
    printf 'No NuGet packages found under `%s`.\n' "$nuget_dir"
  fi

  printf '\n## Tracked Release-Relevant Assets\n\n'
  printf '```text\n'
  git ls-files \
    LICENSE \
    NOTICE \
    README.md \
    docs/nuget/package-readme.md \
    reports/public-release \
    scripts/release \
    .github/workflows/release-nuget-preview.yml
  printf '```\n\n'

  printf '## Tracked Generated Sample WASM Modules\n\n'
  wasm_files="$(git ls-files 'src/samples/**/modules/*.wasm' || true)"
  if [[ -n "$wasm_files" ]]; then
    printf 'Unexpected tracked sample WASM modules:\n\n```text\n%s\n```\n' "$wasm_files"
    exit 1
  fi
  printf 'No tracked generated sample WASM modules.\n'
} > "$report_path"

cat "$report_path"

