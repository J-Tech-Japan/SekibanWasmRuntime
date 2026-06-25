# Runtime Host Preview 3 Release Metadata (SWR-G044)

The corrected public runtime-host preview image is **`1.0.0-preview.3`**, released
from `main` at commit `c7e63cd` (or later). It carries the WASI preview2 shim
(SWR-G042) and the materialized-view sample support (SWR-G043), which the stale
`1.0.0-preview.2` image lacks.

> This document is the **release plan + verification gate**. Publishing a public
> image and moving the `preview` tag are operator/CI actions; they are NOT
> performed by the child implementation loop. Do not claim release readiness until
> the gate below passes.

## Why preview 2 is stale and preview 3 is the corrected tag

- `1.0.0-preview.2` and the moving `preview` tag both point at digest
  `sha256:11a8006f4e6c268231125744f5e93ab92dc06747c9820c377248eed88b5e9e11`.
- That digest is a multi-arch manifest list but its images contain only
  `/app/libwasmtime.so` — **not** `/app/libwasmtime_preview2_shim.so`
  (`WASMTIME_PREVIEW2_SHIM_PATH` is unset). It was built before the SWR-G042 shim
  fix (`8381a5a`). Without the shim, `list-query` and Materialized View catch-up
  throw `DllNotFoundException: ... 'wasmtime_preview2_shim'`.
- `1.0.0-preview.2` is an **immutable** tag (already published and documented), so
  it must not be re-pointed. The corrected image is published as the next
  immutable tag, **`1.0.0-preview.3`**, and `preview` is moved only after the
  preview 3 exact-tag verification passes.

## Reconcile the stuck preview 2 publish run

- Run [`28142753464`](https://github.com/J-Tech-Japan/SekibanWasmRuntime/actions/runs/28142753464)
  was dispatched from `8381a5a` with `image_tag=1.0.0-preview.2`,
  `push=true`. As of this packet it is still `in_progress` and, by design, would
  **re-push the immutable `1.0.0-preview.2` tag** — a tagging-policy conflict.
- **Action (operator/CI):** cancel or let it fail, and **supersede** it with the
  preview 3 publish below. It must NOT be allowed to move `preview` to a
  preview-2 digest. Record its final state (cancelled / failed / superseded)
  before moving `preview`.

  ```bash
  gh run cancel 28142753464 --repo J-Tech-Japan/SekibanWasmRuntime   # if still running
  gh run view 28142753464 --repo J-Tech-Japan/SekibanWasmRuntime --json status,conclusion
  ```

## Publish plan (operator/CI)

Source commit: `c7e63cd` (SWR-G043) or later `main`.

```bash
gh workflow run release-ghcr-image-preview \
  --repo J-Tech-Japan/SekibanWasmRuntime \
  --ref main \
  -f image_tag=1.0.0-preview.3 \
  -f push=true \
  -f update_moving_tag=true
gh run watch <preview3-run-id> --repo J-Tech-Japan/SekibanWasmRuntime --exit-status
```

Expected image refs after a green run:

- `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` (immutable)
- `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:preview` (moved to the same digest)

The publish job builds + pushes a `linux/amd64` + `linux/arm64` manifest list and
its Dockerfile fails closed if either `libwasmtime.so` or
`libwasmtime_preview2_shim.so` is missing/wrong-arch per platform
(SWR-G042). With the validation/publish split (SWR-G042), a manual `push=true`
dispatch goes straight to the publish job — no redundant pre-build.

## Verification gate (fail closed — do not move `preview` until all pass)

```bash
# 1) Multi-arch + per-platform native libraries (libwasmtime.so AND the shim).
IMAGE_TAG=1.0.0-preview.3 scripts/release/verify-runtime-host-multiarch.sh

# 2) preview points at the SAME digest as the immutable preview 3 tag.
docker buildx imagetools inspect ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:preview \
  --format '{{.Manifest.Digest}}'
docker buildx imagetools inspect ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3 \
  --format '{{.Manifest.Digest}}'

# 3) Public-container sample smoke against the EXACT public tag — proves /health,
#    schema-aware /ready, identity, commit, tag-state, list-query, and MV catch-up.
SAMPLE_RUNTIME_IMAGE_TAG=1.0.0-preview.3 \
  bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh
SAMPLE_RUNTIME_IMAGE_TAG=1.0.0-preview.3 \
  bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
```

- The `verify-runtime-host-multiarch.sh` gate (SWR-G040) fails closed on a missing
  per-platform `libwasmtime_preview2_shim.so` — re-using exactly the check that
  catches the preview 2 defect.
- `preview` must be moved to preview 3 **only after** steps 1–3 are green.
- **If GHCR publish or the sample smoke fails, record the blocker here and do NOT
  claim release readiness** — leave `preview` on its prior digest.

## Status

- [ ] Run `28142753464` reconciled (cancelled / failed / superseded) with evidence.
- [ ] `1.0.0-preview.3` published from `c7e63cd` (or later) — run URL + digest recorded.
- [ ] `verify-runtime-host-multiarch.sh` PASS (both platforms, both native libs).
- [ ] `preview` digest == `1.0.0-preview.3` digest.
- [ ] Public-container smoke PASS through list-query + Materialized View read.
- [ ] Docs/sample default updated to recommend `1.0.0-preview.3` (this PR moves the
      docs recommendation; the AppHost default tag flips once the tag is verified).
