#!/bin/sh
# Validate the Swift release tag using only POSIX shell and standard grep.
set -eu

tag=${1-}
case "$tag" in
  swift-v*) version=${tag#swift-v} ;;
  *)
    echo "Tag '$tag' does not match swift-vX.Y.Z" >&2
    exit 1
    ;;
esac

if ! printf '%s\n' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$'; then
  echo "Tag '$tag' does not match swift-vX.Y.Z" >&2
  exit 1
fi

printf 'Release tag: %s (mirror tag v%s)\n' "$tag" "$version"
