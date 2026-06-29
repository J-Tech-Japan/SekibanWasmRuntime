#!/usr/bin/env bash
# Duplicate-version guard for the recurring Rust crates release lane.
#
# crates.io versions are immutable, so a re-run of a release tag must never
# attempt to republish an existing version. This script queries crates.io for
# each public crate at the target version and fails (before any publish) if any
# version already exists, with a clear message instead of partial duplicate
# release noise.
#
# Usage: scripts/release/check-rust-crates-unpublished.sh <version>
set -euo pipefail

VERSION="${1:?usage: check-rust-crates-unpublished.sh <version>}"
CRATES=(sekiban-core sekiban-derive sekiban-wasm sekiban-mv sekiban-executor)
UA="sekiban-wasm-runtime-release-check (+https://github.com/J-Tech-Japan/SekibanWasmRuntime)"

already=()
for crate in "${CRATES[@]}"; do
  # Do not let curl's exit status abort the script; branch on the HTTP code.
  code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 \
    -H "User-Agent: ${UA}" \
    "https://crates.io/api/v1/crates/${crate}/${VERSION}" || true)"
  case "${code}" in
    200)
      echo "ALREADY PUBLISHED: ${crate} ${VERSION}"
      already+=("${crate}")
      ;;
    404)
      echo "ok: ${crate} ${VERSION} is not yet on crates.io"
      ;;
    *)
      echo "ERROR: unexpected HTTP ${code:-<none>} while checking ${crate} ${VERSION} on crates.io" >&2
      exit 2
      ;;
  esac
  # Be gentle with the crates.io API.
  sleep 1
done

if [ "${#already[@]}" -gt 0 ]; then
  echo "ERROR: refusing to publish; these crate versions already exist on crates.io: ${already[*]} @ ${VERSION}" >&2
  echo "crates.io versions are immutable. Use a new release tag/version for ongoing releases." >&2
  exit 1
fi

echo "All ${#CRATES[@]} public Rust crates are clear to publish at ${VERSION}."
