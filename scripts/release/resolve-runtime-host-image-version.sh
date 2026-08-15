#!/usr/bin/env bash
set -euo pipefail

# Resolve the version advertised by the public moving runtime-host tag.  The
# consumer-document checks must compare against the registry image metadata (or
# an image release-lane value), never against a duplicated repository literal.
#
# Usage:
#   RUNTIME_IMAGE_TAG=preview scripts/release/resolve-runtime-host-image-version.sh
#   scripts/release/resolve-runtime-host-image-version.sh preview
#
# The command prints only the version so callers can use it as an independent
# release input.  Every manifest variant must advertise the same version.

cd "$(git rev-parse --show-toplevel)"

image_name="${IMAGE_NAME:-ghcr.io/j-tech-japan/sekiban-wasm-runtime-host}"
image_tag="${RUNTIME_IMAGE_TAG:-${1:-preview}}"
image_ref="${image_name}:${image_tag}"

command -v docker >/dev/null 2>&1 || {
  printf 'docker is required to inspect %s\n' "$image_ref" >&2
  exit 1
}
docker buildx version >/dev/null 2>&1 || {
  printf 'docker buildx is required to inspect %s\n' "$image_ref" >&2
  exit 1
}

inspect_json="$(docker buildx imagetools inspect "$image_ref" --format '{{json .}}')"

version="$(printf '%s\n' "$inspect_json" | python3 -c '
import json
import re
import sys

payload = json.load(sys.stdin)
images = payload.get("image", {})
versions = {
    details.get("config", {}).get("Labels", {}).get("org.opencontainers.image.version", "")
    for details in images.values()
}
versions.discard("")
if not versions:
    raise SystemExit("registry image has no org.opencontainers.image.version label")
if len(versions) != 1:
    raise SystemExit(f"registry image has inconsistent advertised versions: {sorted(versions)!r}")
version = next(iter(versions))
if not re.fullmatch(r"1\.0\.0-preview\.[0-9A-Za-z][0-9A-Za-z.-]*", version):
    raise SystemExit(f"registry image advertised an invalid preview version: {version!r}")
print(version)
')"

printf '%s\n' "$version"
