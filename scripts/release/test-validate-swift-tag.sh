#!/bin/sh
# Regression test for validate-swift-tag.sh. Keep this POSIX so it proves the
# same shell contract used by the GitHub Actions gate.
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
validator="$script_dir/validate-swift-tag.sh"

expect_valid() {
  if ! "$validator" "$1" >/dev/null 2>&1; then
    echo "expected valid tag: $1" >&2
    exit 1
  fi
}

expect_invalid() {
  if "$validator" "$1" >/dev/null 2>&1; then
    echo "expected invalid tag: $1" >&2
    exit 1
  fi
}

expect_valid swift-v0.1.0
expect_valid swift-v1.0.0-preview.4
expect_valid swift-v1.0.0+build.7

expect_invalid v0.1.0
expect_invalid swift-0.1.0
expect_invalid swift-v0.1
expect_invalid swift-v1.2.3.4
expect_invalid swift-vX.Y.Z
expect_invalid swift-v
expect_invalid ""
expect_invalid release-swift-v0.1.0

echo "Swift tag validation tests passed"
