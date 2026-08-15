#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

package_version="${PACKAGE_VERSION:-${1:-1.0.0-preview.5}}"
package_version="${package_version#v}"
runtime_image_version="${RUNTIME_IMAGE_VERSION:-1.0.0-preview.3}"
nuget_dir="${NUGET_OUTPUT_DIR:-artifacts/nuget}"

if [[ ! "$package_version" =~ ^1\.0\.0-preview\.[0-9A-Za-z.-]+$ ]]; then
  printf 'Package version must be 1.0.0-preview.*; got %s\n' "$package_version" >&2
  exit 1
fi

expected_packages=(
  "Sekiban.Dcb.WasmRuntime"
  "Sekiban.Dcb.WasmRuntime.Remote"
  "Sekiban.Dcb.WasmRuntime.Wasmtime"
  "Sekiban.Dcb.WasmRuntime.Aspire"
)

need_pack=false
for package_id in "${expected_packages[@]}"; do
  if [[ ! -f "$nuget_dir/$package_id.$package_version.nupkg" ]]; then
    need_pack=true
    break
  fi
done

if [[ "$need_pack" == true ]]; then
  PACKAGE_VERSION="$package_version" \
    NUGET_OUTPUT_DIR="$nuget_dir" \
    RELEASE_REPORT_DIR="${RELEASE_REPORT_DIR:-artifacts/release}" \
    scripts/release/inspect-nuget-packages.sh "$package_version" >/dev/null
fi

python3 scripts/release/check-consumer-version-accuracy.py \
  --package-version "$package_version" \
  --package-dir "$nuget_dir" \
  --runtime-image-version "$runtime_image_version"
