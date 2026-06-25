#!/usr/bin/env bash
set -euo pipefail

# Verify that a PUBLISHED runtime-host GHCR tag is a coherent multi-arch image:
# one public tag whose manifest list contains both linux/amd64 and linux/arm64,
# each pullable, each carrying its own correct-architecture Wasmtime native
# library. This is the preview 2 release-readiness gate (SWR-G040); it does NOT
# publish anything — it inspects an already-pushed tag and FAILS CLOSED on any
# missing platform, missing manifest entry, or missing/mismatched native asset.
#
# Usage:
#   IMAGE_TAG=1.0.0-preview.2 scripts/release/verify-runtime-host-multiarch.sh
#   scripts/release/verify-runtime-host-multiarch.sh 1.0.0-preview.2
#
# Knobs:
#   IMAGE_NAME             default ghcr.io/j-tech-japan/sekiban-wasm-runtime-host
#   RELEASE_REPORT_DIR     default artifacts/release
#   SKIP_NATIVE_ASSET_CHECK=1  inspect the manifest + pulls only (native asset
#                              check needs `docker create`/`cp` + `od`).

cd "$(git rev-parse --show-toplevel)"

image_name="${IMAGE_NAME:-ghcr.io/j-tech-japan/sekiban-wasm-runtime-host}"
image_tag="${IMAGE_TAG:-${1:-1.0.0-preview.2}}"
image_tag="${image_tag#v}"
image_ref="${image_name}:${image_tag}"

report_dir="${RELEASE_REPORT_DIR:-artifacts/release}"
report_path="$report_dir/runtime-host-multiarch-verification.md"
work_dir="$(mktemp -d)"
mkdir -p "$report_dir"

required_platforms=(linux/amd64 linux/arm64)

# ELF e_machine (little-endian, offset 0x12): 0x3e = x86-64, 0xb7 = AArch64.
# Plain case lookup (not an associative array) so this runs on bash 3.2 (macOS).
want_machine_for() {
  case "$1" in
    linux/amd64) printf '3e00' ;;
    linux/arm64) printf 'b700' ;;
    *) printf 'unknown' ;;
  esac
}

result="PASS"
declare -a findings=()
manifest_platforms=""

fail() { result="FAIL"; findings+=("FAIL: $*"); }
ok()   { findings+=("OK: $*"); }

cleanup() { rm -rf "$work_dir"; }
trap cleanup EXIT

write_report() {
  {
    printf '# Runtime Host Multi-Arch Verification (SWR-G040)\n\n'
    printf '%s\n' "- Image: \`$image_ref\`"
    printf '%s\n' "- Result: **$result**"
    printf '%s\n' "- Required platforms: \`${required_platforms[*]}\`"
    printf '%s\n' "- Observed manifest platforms: \`${manifest_platforms:-none}\`"
    printf '%s\n' "- Commit: \`$(git rev-parse HEAD 2>/dev/null || echo unknown)\`"
    printf '\n## Checks\n\n'
    for line in "${findings[@]}"; do printf '%s\n' "- $line"; done
    printf '\n## Reproduce\n\n'
    printf '```bash\n'
    printf 'docker buildx imagetools inspect %s\n' "$image_ref"
    for p in "${required_platforms[@]}"; do
      printf 'docker pull --platform %s %s\n' "$p" "$image_ref"
    done
    printf '```\n'
  } > "$report_path"
}

finish() {
  write_report
  cat "$report_path"
  [[ "$result" == "PASS" ]] || exit 1
}

command -v docker >/dev/null 2>&1 || { fail "docker is not available"; finish; }
docker buildx version >/dev/null 2>&1 || { fail "docker buildx is not available"; finish; }

# 1) Manifest list must contain every required platform.
if ! manifest_platforms="$(docker buildx imagetools inspect "$image_ref" \
  --format '{{range .Manifest.Manifests}}{{.Platform.OS}}/{{.Platform.Architecture}} {{end}}' 2>"$work_dir/inspect.err")"; then
  fail "could not inspect $image_ref: $(tr '\n' ' ' < "$work_dir/inspect.err")"
  finish
fi
for p in "${required_platforms[@]}"; do
  case " $manifest_platforms " in
    *" $p "*) ok "manifest list contains $p" ;;
    *) fail "manifest list is missing $p (got: ${manifest_platforms:-none})" ;;
  esac
done

# Native libraries that must be present AND arch-correct in each image variant.
# libwasmtime.so is the Wasmtime C API; libwasmtime_preview2_shim.so is the WASI
# preview2 shim the runtime DllImports for list-query / projection catch-up
# (missing it throws DllNotFoundException at runtime even though /health passes).
required_libs=(/app/libwasmtime.so /app/libwasmtime_preview2_shim.so)

# 2) Each required platform must be pullable, and (unless skipped) carry every
#    required native library with the correct architecture.
for p in "${required_platforms[@]}"; do
  if ! docker pull --platform "$p" "$image_ref" >"$work_dir/pull.log" 2>&1; then
    fail "$p pull failed: $(tail -n1 "$work_dir/pull.log")"
    continue
  fi
  ok "$p pull succeeded"

  [[ "${SKIP_NATIVE_ASSET_CHECK:-0}" == "1" ]] && continue

  cid="$(docker create --platform "$p" "$image_ref" 2>/dev/null || true)"
  if [[ -z "$cid" ]]; then fail "$p: could not create a container to inspect native assets"; continue; fi
  want="$(want_machine_for "$p")"
  for path in "${required_libs[@]}"; do
    local_copy="$work_dir/$(basename "$path")-${p//\//-}"
    if ! docker cp "$cid:$path" "$local_copy" >/dev/null 2>&1; then
      fail "$p: $path missing in the image"
      continue
    fi
    machine="$(od -An -tx1 -j18 -N2 "$local_copy" | tr -d ' ')"
    if [[ "$machine" == "$want" ]]; then
      ok "$p: $path present and arch-correct (e_machine=$machine)"
    else
      fail "$p: $path e_machine=$machine, expected $want"
    fi
  done
  docker rm "$cid" >/dev/null 2>&1 || true
done

finish
