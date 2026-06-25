# Runtime Host Preview 3 Release Metadata (SWR-G044)

The corrected public runtime-host preview image is planned as
**`1.0.0-preview.3`**, to be published from `main` at commit `c7e63cd` (or later)
after this metadata lands. It must carry the WASI preview2 shim (SWR-G042) and
the materialized-view sample support (SWR-G043), which the stale
`1.0.0-preview.2` image lacks.

> This document is the **release metadata preparation plan**. Publishing a public
> image and moving the `preview` tag are operator/CI actions after this PR lands
> on `main`; they are NOT performed by the child implementation loop. The
> post-publish public artifact verification belongs to SWR-G045. Do not claim
> release readiness until that follow-up verification passes.

## Why preview 2 is stale and preview 3 is the corrected tag

- `1.0.0-preview.2` and the moving `preview` tag currently point at digest
  `sha256:0d5c4fe1bb72dc3fb6fbf80ac5e671ae324cf2be09cda7d02c50ce91c33f8cf7`
  (this changed from the earlier `sha256:11a8006f…`; the immutable tag was
  re-pointed — see the run reconciliation below).
- That digest is a multi-arch manifest list (`linux/amd64` + `linux/arm64`) but its
  images **still** contain only `/app/libwasmtime.so` — **not**
  `/app/libwasmtime_preview2_shim.so` (`WASMTIME_PREVIEW2_SHIM_PATH` is unset;
  verified by `docker run --entrypoint /bin/sh`). The image content predates the
  SWR-G042 shim fix (`8381a5a`). Without the shim, `list-query` and Materialized
  View catch-up throw `DllNotFoundException: ... 'wasmtime_preview2_shim'`.
- `1.0.0-preview.2` is an **immutable** tag, so it should not be re-pointed (it
  already was, to another shim-less digest). The corrected, shim-carrying image
  should be published as the next immutable tag, **`1.0.0-preview.3`**, and
  `preview` is moved only after the preview 3 exact-tag verification passes.

## Reconcile the stuck preview 2 publish run

- Run [`28142753464`](https://github.com/J-Tech-Japan/SekibanWasmRuntime/actions/runs/28142753464)
  was dispatched from `8381a5a` with `image_tag=1.0.0-preview.2`,
  `push=true`. By design it would have **re-pushed the immutable `1.0.0-preview.2`
  tag** — a tagging-policy conflict.
- **Reconciled (AC3): the run `completed` with conclusion `failure`** (updated
  `2026-06-25T04:54:10Z`); no cancellation was needed. Note, however, that
  `1.0.0-preview.2` + `preview` **did move** to digest `sha256:0d5c4fe1…`
  (from `sha256:11a8006f…`) — the immutable preview-2 tag was re-pointed, which is
  itself a tagging-policy concern. The new digest is **still shim-less** (verified:
  amd64 image has `/app/libwasmtime.so` only, no preview2 shim), so it does not
  resolve `list-query` / MV. The corrected, shim-carrying release therefore still
  proceeds as `1.0.0-preview.3` below.

  ```bash
  $ gh run view 28142753464 --repo J-Tech-Japan/SekibanWasmRuntime \
      --json status,conclusion
  {"status":"completed","conclusion":"failure"}
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

## Post-publish verification gate (SWR-G045; fail closed)

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
- `preview` must point to preview 3 only after the exact-tag verification is
  green.
- **If GHCR publish or the sample smoke fails, record the blocker here and do NOT
  claim release readiness** — leave `preview` on its prior digest.
- SWR-G045 owns recording the actual run URL/digest, verifying the public tag,
  and changing docs/sample wording from planned to verified.

## Status

> **Release readiness IS now claimed for `1.0.0-preview.3`.** The operator published
> the corrected tag from merged `main`, and SWR-G045 verified the actual published
> artifact (multi-arch + both native libraries, `preview` pointing at the same
> digest, and the public-container smoke green through `list-query` + Materialized
> View). Full evidence:
> [`runtime-host-preview-3-release-verification.md`](runtime-host-preview-3-release-verification.md).

- [x] Run `28142753464` reconciled — `completed` / `conclusion: failure`.
  `1.0.0-preview.2` + `preview` moved to `sha256:0d5c4fe1…` (the immutable tag was
  re-pointed) but the new digest is **still shim-less** (verified), so it does not
  fix `list-query` / MV.
- [x] Operator published `1.0.0-preview.3` from merged `main` (commit `9441260`) —
  run [`28160428104`](https://github.com/J-Tech-Japan/SekibanWasmRuntime/actions/runs/28160428104),
  digest `sha256:8bdebccd…`. The run is marked `failure` (build-step timeout on the
  emulated arm64 leg) but the corrected multi-arch, shim-carrying bytes were pushed;
  SWR-G045 verifies the bytes directly, not the run conclusion.
- [x] SWR-G045 records `verify-runtime-host-multiarch.sh` PASS (both platforms,
  both native libs including `libwasmtime_preview2_shim.so`).
- [x] SWR-G045 records `preview` digest == `1.0.0-preview.3` digest
  (`sha256:8bdebccd…`).
- [x] SWR-G045 records public-container smoke PASS through `list-query` +
  Materialized View read.
- [x] SWR-G045 updates docs/sample default to recommend `1.0.0-preview.3` now that
  the public tag is verified (this doc, `docs/quickstart.md`, the docker README, the
  sample README, and the AppHost default tag).
