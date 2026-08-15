#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

template_package_version="${TEMPLATE_PACKAGE_VERSION:?TEMPLATE_PACKAGE_VERSION must identify the produced Templates nupkg}"
runtime_package_version="${RUNTIME_PACKAGE_VERSION:?RUNTIME_PACKAGE_VERSION must be supplied independently of TEMPLATE_PACKAGE_VERSION}"
dcb_version="${SEKIBAN_DCB_VERSION:?SEKIBAN_DCB_VERSION must be supplied independently of the packaged README}"
runtime_image_version="${RUNTIME_IMAGE_VERSION:?RUNTIME_IMAGE_VERSION must come from the registry or runtime-host release lane}"
template_package_path="${TEMPLATE_PACKAGE_PATH:-${TEMPLATE_PACKAGE_DIR:-artifacts/packages}/Sekiban.Dcb.WasmRuntime.Templates.${template_package_version}.nupkg}"

if [[ ! "$template_package_version" =~ ^1\.0\.0-preview\.[0-9A-Za-z.-]+$ ]]; then
  printf 'Templates package version must be 1.0.0-preview.*; got %s\n' "$template_package_version" >&2
  exit 1
fi

if [[ ! -f "$template_package_path" ]]; then
  printf 'Produced Templates nupkg was not found: %s\n' "$template_package_path" >&2
  exit 1
fi

python3 scripts/release/check-consumer-version-accuracy.py \
  --package-version "$runtime_package_version" \
  --package-file "$template_package_path" \
  --package-id "Sekiban.Dcb.WasmRuntime.Templates" \
  --artifact-version "$template_package_version" \
  --dcb-version "$dcb_version" \
  --runtime-image-version "$runtime_image_version" \
  --document docs/nuget/templates-package-readme.md
