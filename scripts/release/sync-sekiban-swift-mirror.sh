#!/usr/bin/env bash
# SWR-G062 sekiban-swift mirror sync.
#
# SwiftPM resolves packages from a repository root, so the Swift SDK is
# published through the mirror repository github.com/J-Tech-Japan/sekiban-swift
# whose content is exactly the package tree at src/wasm-projectors/swift
# (monorepo is the source of truth; the mirror is write-only from this script).
#
# Modes:
#   --dry-run  Stage the mirror tree locally, guard it against host-repo
#              relative path references, and validate it with `swift build`
#              and `swift test` inside the staged tree. No mirror repository,
#              network access to it, or token needed.
#   --push     Additionally clone the mirror, replace its content with the
#              staged tree, commit, push, and tag. BLOCKED on human
#              prerequisites (mirror repo creation + SEKIBAN_SWIFT_MIRROR_TOKEN);
#              fails fast with a clear message until they exist.
#
# Usage:
#   bash scripts/release/sync-sekiban-swift-mirror.sh --dry-run
#   SEKIBAN_SWIFT_MIRROR_TOKEN=... bash scripts/release/sync-sekiban-swift-mirror.sh --push --version 0.1.0
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"
ROOT="$(pwd)"

PACKAGE_DIR="$ROOT/src/wasm-projectors/swift"
STAGE_ROOT="${SEKIBAN_SWIFT_MIRROR_STAGE_DIR:-$ROOT/artifacts/sekiban-swift-mirror}"
STAGE_DIR="$STAGE_ROOT/tree"
MIRROR_REPO="${SEKIBAN_SWIFT_MIRROR_REPO:-J-Tech-Japan/sekiban-swift}"

MODE=""
VERSION=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run) MODE="dry-run"; shift ;;
    --push) MODE="push"; shift ;;
    --version) VERSION="${2:-}"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
if [[ -z "$MODE" ]]; then
  echo "usage: $0 --dry-run | --push [--version X.Y.Z]" >&2
  exit 2
fi

log() { printf '[sync-sekiban-swift-mirror] %s\n' "$*"; }
fail() { log "FAIL: $*"; exit 1; }

command -v swift >/dev/null 2>&1 || fail "swift toolchain not found"

# ---------------------------------------------------------------------------
# 1. Stage the mirror tree: exactly what the mirror repository root contains.
# ---------------------------------------------------------------------------

rm -rf "$STAGE_ROOT"
mkdir -p "$STAGE_DIR"

for entry in Package.swift README.md LICENSE Sources Tests; do
  [[ -e "$PACKAGE_DIR/$entry" ]] || fail "missing $entry in $PACKAGE_DIR"
  cp -R "$PACKAGE_DIR/$entry" "$STAGE_DIR/$entry"
done
log "staged mirror tree at ${STAGE_DIR#"$ROOT"/}"

# ---------------------------------------------------------------------------
# 2. Guard: the staged tree must be self-contained. Any host-repo relative
#    path reference (path-based package dependencies, parent-directory
#    escapes, monorepo paths in the manifest) would break the mirror.
# ---------------------------------------------------------------------------

leaks="$(grep -RnE '\.package\(\s*(name:[^,]+,\s*)?path:' "$STAGE_DIR" --include='Package.swift' || true)"
[[ -z "$leaks" ]] || fail "staged Package.swift declares path-based dependencies: $leaks"

leaks="$(grep -Rn '\.\./' "$STAGE_DIR/Package.swift" || true)"
[[ -z "$leaks" ]] || fail "staged Package.swift references parent directories: $leaks"

leaks="$(grep -RnE 'src/(wasm-projectors|samples|lib|runtime)/' "$STAGE_DIR/Package.swift" || true)"
[[ -z "$leaks" ]] || fail "staged Package.swift references monorepo paths: $leaks"

log "guard OK: no host-repo relative path references in the staged manifest"

# ---------------------------------------------------------------------------
# 3. Validate: the staged tree must build and test standalone, exactly as an
#    external consumer would receive it.
# ---------------------------------------------------------------------------

log "swift build (staged tree)"
swift build --package-path "$STAGE_DIR" || fail "swift build failed inside the staged tree"
log "swift test (staged tree)"
swift test --package-path "$STAGE_DIR" || fail "swift test failed inside the staged tree"

if [[ "$MODE" == "dry-run" ]]; then
  log "DRY-RUN PASS: staged tree is self-contained and builds/tests standalone"
  exit 0
fi

# ---------------------------------------------------------------------------
# 4. Push mode — BLOCKED on human prerequisites until the mirror repository
#    and its token exist (see docs/release/swift-sdk-release-lane.md).
# ---------------------------------------------------------------------------

[[ -n "$VERSION" ]] || fail "--push requires --version X.Y.Z (mirror tag is v<version>)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$ ]] || fail "invalid version: $VERSION"
if [[ -z "${SEKIBAN_SWIFT_MIRROR_TOKEN:-}" ]]; then
  fail "SEKIBAN_SWIFT_MIRROR_TOKEN is not set. Mirror publication is blocked on human prerequisites: create github.com/$MIRROR_REPO and provision a push token (see docs/release/swift-sdk-release-lane.md)."
fi

CLONE_DIR="$STAGE_ROOT/mirror-clone"
SOURCE_COMMIT="$(git rev-parse HEAD)"
log "cloning https://github.com/$MIRROR_REPO"
git clone --depth 1 "https://x-access-token:${SEKIBAN_SWIFT_MIRROR_TOKEN}@github.com/$MIRROR_REPO.git" "$CLONE_DIR" \
  || fail "could not clone the mirror repository (does github.com/$MIRROR_REPO exist yet?)"

find "$CLONE_DIR" -mindepth 1 -maxdepth 1 -not -name '.git' -exec rm -rf {} +
cp -R "$STAGE_DIR"/. "$CLONE_DIR"/
(
  cd "$CLONE_DIR"
  git add -A
  if git diff --cached --quiet; then
    log "mirror already up to date with $SOURCE_COMMIT"
  else
    git -c user.name="sekiban-swift-mirror-sync" -c user.email="noreply@j-tech.co.jp" \
      commit -m "Sync from SekibanWasmRuntime@$SOURCE_COMMIT"
    git push origin HEAD
  fi
  git tag "v$VERSION"
  git push origin "v$VERSION"
) || fail "mirror push failed"

log "PUSH PASS: mirror synced from $SOURCE_COMMIT and tagged v$VERSION"
exit 0
