# Runtime Host Preview 2 Multi-Arch Release Checklist

This checklist prepares **preview 2** of the public runtime-host GHCR image so it
is released as **one coherent multi-arch tag** for both `linux/amd64` and
`linux/arm64`. It is the runtime-host **image** lane checklist; it is separate
from the NuGet package checklist
([`nuget-preview-release-checklist.md`](nuget-preview-release-checklist.md)).

Preview 1 shipped amd64-only: on Apple Silicon a plain pull of
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1` fails with
`no matching manifest for linux/arm64/v8`. Preview 2 must fix that by publishing
a single tag whose manifest list contains both platforms — **not** separate
architecture-specific tags or release lines.

The underlying multi-arch build/publish capability is
[SWR-G039](ghcr-image-preview.md#platform-support); this checklist is the
release-readiness wrapper around it. Preparing this checklist does **not**
publish preview 2.

## Publish status (SWR-G042) — fail closed

> **Preview 2 published, but it needs a re-publish: readiness is NOT complete.**
>
> The manual `push=true` dispatch (run `28137575387`) **succeeded** — the workflow
> operability fix landed and `1.0.0-preview.2` + moving `preview` point at the same
> digest (`sha256:11a8006f4e6c268231125744f5e93ab92dc06747c9820c377248eed88b5e9e11`),
> a multi-arch manifest list with both `linux/amd64` and `linux/arm64`, and both
> platform pulls succeed (Apple Silicon pulls without `DOCKER_DEFAULT_PLATFORM`).
>
> The sample smoke against `SAMPLE_RUNTIME_IMAGE_TAG=1.0.0-preview.2` confirmed
> SWR-G041: `/health`, schema-aware `/ready`, runtime identity, commit, and
> tag-state read all pass on fresh Postgres. **But `list-query` / projection
> catch-up failed** with
> `System.DllNotFoundException: Unable to load shared library 'wasmtime_preview2_shim'`:
> the published image carried `/app/libwasmtime.so` but **not** the WASI preview2
> shim.
>
> **Fix in this PR (SWR-G042):** the runtime-host image now compiles the Rust
> `wasmtime-preview2-shim` for **both** platforms, ships it at
> `/app/libwasmtime_preview2_shim.so`, sets `WASMTIME_PREVIEW2_SHIM_PATH`, and
> **fails the build closed** if either `libwasmtime.so` or the shim is missing or
> the wrong architecture (per Buildx leg). `scripts/release/verify-runtime-host-multiarch.sh`
> now also fails closed on a missing/wrong-arch shim per platform. Verified
> locally on `linux/arm64`: both libraries present and arch-correct, and `ldd`
> resolves the shim's dependencies in the runtime image (so it loads).
>
> **Remaining operator step (outward-facing, not run by automation): re-publish**
> preview 2 from this commit, then verify including `list-query` —
>
> ```bash
> gh workflow run release-ghcr-image-preview --repo J-Tech-Japan/SekibanWasmRuntime \
>   --ref main -f image_tag=1.0.0-preview.2 -f push=true -f update_moving_tag=true
> gh run watch <run-id> --repo J-Tech-Japan/SekibanWasmRuntime --exit-status
> IMAGE_TAG=1.0.0-preview.2 scripts/release/verify-runtime-host-multiarch.sh
> bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh
> SAMPLE_RUNTIME_IMAGE_TAG=1.0.0-preview.2 \
>   bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
> ```
>
> Do **not** mark preview 2 readiness complete until the re-published
> `1.0.0-preview.2` tag passes the multi-arch + per-platform shim verification
> **and** the sample smoke is green through **`list-query`** (not just commit /
> tag-state read) against that tag. Because preview 2 is an immutable tag, a fixed
> image may need a new immutable tag (e.g. `1.0.0-preview.3`) with `preview` moved
> to it.

## Tag Contract

- [ ] Immutable preview 2 tag is `1.0.0-preview.2`, published as
  `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.2`.
- [ ] The publish is driven by a `runtime-host-v1.0.0-preview.2` git tag on the
  canonical repository (the image release lane), or an explicit guarded
  `workflow_dispatch` with `push: true`.
- [ ] The moving `preview` tag
  (`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:preview`) is updated to the
  preview 2 build (automatic on the tag lane; opt-in `update_moving_tag` on
  dispatch). No `latest` tag is published.
- [ ] Both the immutable `1.0.0-preview.2` tag and the moving `preview` tag are
  multi-arch manifest lists (verified below), not single-platform images.

## Separate Lanes and Cross-Lane Ordering

- [ ] NuGet and GHCR remain **separate release lanes**: the runtime-host image
  lane is never triggered by the NuGet `release` event, and this checklist
  changes nothing about NuGet package contents, package IDs, or Trusted
  Publishing.
- [ ] If preview 2 ships both NuGet packages and the runtime-host image, the
  cross-lane ordering is explicit: the image at a given commit is compatible
  with the `Sekiban.Dcb.WasmRuntime*` preview packages built from the **same**
  source tree (see
  [`ghcr-image-preview.md`](ghcr-image-preview.md#source-commit-traceability-and-compatible-baseline)).
  Record which lane is published first; neither lane blocks the other.

## Pre-Publish Build Gates (fail closed)

- [ ] The full `linux/amd64` + `linux/arm64` build passed at the **publish /
  manual gate** — either the `publish` job's own multi-arch build, or an opt-in
  no-push `workflow_dispatch` full validation. (Ordinary `pull_request`
  validation is a fast native-`amd64` build and does **not** exercise the arm64
  leg, so a green PR alone is not sufficient evidence of a working arm64 image.)
- [ ] The Dockerfile per-platform native-asset assertion passed for both legs:
  **both** `/app/libwasmtime.so` **and** `/app/libwasmtime_preview2_shim.so`
  present with an ELF `e_machine` matching the target arch (`0x3e` x86-64 /
  `0xb7` AArch64). A missing or wrong-arch native asset (including the preview2
  shim) fails the build.
- [ ] No partial-platform publish is acceptable: if either architecture image
  fails to build, **do not** publish preview 2.

## Post-Publish Verification (fail closed)

Run the runtime-host multi-arch verification against the published preview 2 tag:

```bash
IMAGE_TAG=1.0.0-preview.2 scripts/release/verify-runtime-host-multiarch.sh
```

The script writes `artifacts/release/runtime-host-multiarch-verification.md` and
exits non-zero (fail closed) if any check below fails.

- [ ] **Manifest inspection** proves both platforms are present on the one public
  tag:

  ```bash
  docker buildx imagetools inspect ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.2
  # Expect Manifests entries for linux/amd64 AND linux/arm64.
  ```

- [ ] **Platform-specific pull evidence** for both architectures:

  ```bash
  docker pull --platform linux/amd64 ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.2
  docker pull --platform linux/arm64 ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.2
  ```

- [ ] **Apple Silicon, no emulation**: on an arm64 host, a *plain* pull and run
  succeeds without `DOCKER_DEFAULT_PLATFORM=linux/amd64`:

  ```bash
  docker pull ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.2
  ```

- [ ] **Per-platform native assets** are present and arch-correct in each image
  variant — the verification script checks **both** `/app/libwasmtime.so` **and**
  `/app/libwasmtime_preview2_shim.so` (ELF `e_machine`) for each platform and
  fails closed on a missing or wrong-arch library, including the preview2 shim.
- [ ] **Sample smoke is green through `list-query`** (not just commit / tag-state
  read) against the tag — proves the preview2 shim loads:

  ```bash
  bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh
  SAMPLE_RUNTIME_IMAGE_TAG=1.0.0-preview.2 \
    bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
  ```

- [ ] The verification report records `Result: **PASS**` for the preview 2 tag.

### Fail-closed conditions

Treat any of the following as release-blocking — do **not** announce preview 2
as multi-arch until resolved:

- [ ] The manifest list is missing `linux/amd64` or `linux/arm64`.
- [ ] Either platform image fails to pull.
- [ ] A platform image is missing `/app/libwasmtime.so` **or**
  `/app/libwasmtime_preview2_shim.so`, or either is built for the wrong
  architecture.
- [ ] The sample smoke fails at `list-query` (the preview2 shim does not load:
  `DllNotFoundException: ... 'wasmtime_preview2_shim'`).
- [ ] Only one architecture published (partial publication).
- [ ] An unresolved smoke gate (see Known Limitations) is being hidden rather
  than disclosed.

## Known Limitations and Migration

- [ ] Release notes state plainly what preview 2 supports (multi-arch
  `linux/amd64` + `linux/arm64` runtime-host image) and what remains
  known-limited.
- [ ] The **public-container sample live commit smoke** previously failed on a
  Postgres schema-initialization gap (DCB tables not created before the first
  commit, `42P01: relation "dcb_events" does not exist`). This is **fixed in the
  runtime host** (SWR-G041: the host now runs EF migration for Postgres at
  startup and `/ready` is schema-fail-closed) — see
  [`runtime-host-postgres-schema-smoke.md`](runtime-host-postgres-schema-smoke.md).
  The fix requires a **republished runtime-host container**: do not claim preview
  2 readiness until the **commit/query smoke is green against the preview 2
  image**, not only `/health`. Multi-arch alone does not fix this.
- [ ] Migration note for preview 1 users: preview 1
  (`1.0.0-preview.1`) is **amd64-only**. Apple Silicon users on preview 1 needed
  `--platform linux/amd64` / `DOCKER_DEFAULT_PLATFORM=linux/amd64`; on preview 2
  they should pull the new multi-arch tag directly and drop the override. The
  amd64 override text is retained in docs **only** as a workaround for older
  amd64-only preview tags.

## Docs Cross-Checks

- [ ] [`ghcr-image-preview.md`](ghcr-image-preview.md) Platform support section
  reflects multi-arch reality.
- [ ] [`../quickstart.md`](../quickstart.md), the
  [public local runtime container README](../../docker/sekiban-wasm-runtime/README.md),
  and the
  [public-container sample README](../../src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/README.md)
  point at the preview 2 multi-arch tag for arm64-native consumption and keep the
  amd64 override only as a legacy-tag workaround.
- [ ] `git diff --check` passes before the release PR is merged.
