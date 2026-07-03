#!/usr/bin/env bash
# Linux-container Swift build feasibility check (SWR-G063).
#
# Stages the sekiban-swift mirror tree (SWR-G062 sync dry-run) and runs
# `swift build` + `swift test` against it inside a swift:6.x Linux container.
# Records works / works-with-caveats / unsupported evidence for
# docs/release/swift-sdk-release-lane.md; the consumer sample itself only
# builds for the wasm target (its linker flags are wasm-ld specific), so the
# package is the meaningful Linux build target.
#
# Exit 0 on PASS or SKIP (Docker unavailable), 1 on build failure.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

IMAGE="${SWIFT_LINUX_IMAGE:-swift:6.1-noble}"
STAGE="$ROOT/artifacts/sekiban-swift-mirror/tree"
REPORT_DIR="$ROOT/reports/smoke"
REPORT="$REPORT_DIR/sekiban-swift-linux-build.md"

log() { printf '[linux-build-check] %s\n' "$*"; }

write_report() {
  local result="$1" detail="$2"
  mkdir -p "$REPORT_DIR"
  {
    printf '# sekiban-swift Linux Container Build Check (SWR-G063)\n\n'
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Detail: $detail"
    printf '%s\n' "- Image: \`$IMAGE\`"
    printf '%s\n' "- Package: staged sekiban-swift mirror tree (SWR-G062 sync dry-run output)"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
  } > "$REPORT"
  log "report: ${REPORT#$ROOT/}"
}

if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
  log "SKIP: Docker is not available."
  write_report "SKIP" "Docker is not available in this environment."
  exit 0
fi

log "staging the mirror tree"
if ! bash scripts/release/sync-sekiban-swift-mirror.sh --dry-run >/dev/null 2>&1; then
  log "FAIL: mirror sync dry-run failed"
  write_report "FAIL" "mirror sync dry-run failed; cannot stage the package tree."
  exit 1
fi

log "running swift build + swift test inside $IMAGE"
if docker run --rm -v "$STAGE:/pkg" -w /pkg "$IMAGE" \
  bash -lc "swift build 2>&1 && swift test 2>&1" > /tmp/sekiban-swift-linux-build.log 2>&1; then
  tail -3 /tmp/sekiban-swift-linux-build.log
  log "PASS: package builds and tests on Linux ($IMAGE)"
  write_report "PASS" "swift build and swift test succeed inside $IMAGE against the staged mirror tree ($(tail -1 /tmp/sekiban-swift-linux-build.log | tr -d '\r'))."
  exit 0
fi

tail -20 /tmp/sekiban-swift-linux-build.log
log "FAIL: Linux container build failed (see /tmp/sekiban-swift-linux-build.log)"
write_report "FAIL" "swift build/test failed inside $IMAGE; see the workflow/console log. Remediation is follow-up work per SWR-G063 scope."
exit 1
