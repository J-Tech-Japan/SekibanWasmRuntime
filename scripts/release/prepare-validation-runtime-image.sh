#!/usr/bin/env bash
# Build the runtime-host image from exact-head source and tag it with the lane
# consumer reference (ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<version>).
#
# Unpublished preview lanes exercise Aspire/template/cargo-sekiban smokes against
# this locally built image instead of pulling an immutable GHCR tag that does not
# exist yet. Post-publish operator runs can keep using the public registry tag.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
ROOT="$(pwd)"

IMAGE_NAME="${IMAGE_NAME:-ghcr.io/j-tech-japan/sekiban-wasm-runtime-host}"
IMAGE_TAG="${RUNTIME_IMAGE_TAG:-${RUNTIME_PACKAGE_VERSION:-${PACKAGE_VERSION:-1.0.0-preview.7}}}"
IMAGE_TAG="${IMAGE_TAG#v}"
IMAGE_REF="${IMAGE_NAME}:${IMAGE_TAG}"

log() { printf '[prepare-validation-runtime-image] %s\n' "$*"; }
fail() { log "FAIL: $*"; exit 1; }

command -v docker >/dev/null 2>&1 || fail "docker required"
docker info >/dev/null 2>&1 || fail "docker daemon unavailable"

log "building runtime host from source → ${IMAGE_REF}"
docker build \
  -f src/runtime/Sekiban.Dcb.WasmRuntime.Host/Dockerfile \
  -t "${IMAGE_REF}" \
  "${ROOT}" || fail "docker build failed"

image_id="$(docker image inspect "${IMAGE_REF}" --format '{{.Id}}' 2>/dev/null || true)"
if [[ -n "$image_id" ]]; then
  log "ready: ${IMAGE_REF} (${image_id})"
else
  log "ready: ${IMAGE_REF}"
fi
