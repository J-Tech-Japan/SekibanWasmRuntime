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

## Publish status (SWR-G042) — operator step, fail closed

> **Preview 2 is NOT yet published and preview 2 readiness is NOT complete.**
> A manual `push=true` dispatch (run `28137575387`) stalled because the workflow
> built the slow `linux/arm64`-under-QEMU leg **twice** — a no-push validation
> build before the publish build — with no timeout. That operability bug is
> **fixed** (SWR-G042): the validation `build` job is now skipped on the publish
> path, the `publish` job has no `needs: build`, both jobs have explicit
> `timeout-minutes`, and they share a GitHub Actions build cache. See
> [`ghcr-image-preview.md`](ghcr-image-preview.md#workflow).
>
> **Remaining operator step (outward-facing, not run by automation):** trigger the
> publish and verify the result —
>
> ```bash
> gh workflow run release-ghcr-image-preview --repo J-Tech-Japan/SekibanWasmRuntime \
>   --ref main -f image_tag=1.0.0-preview.2 -f push=true -f update_moving_tag=true
> gh run watch <run-id> --repo J-Tech-Japan/SekibanWasmRuntime --exit-status
> IMAGE_TAG=1.0.0-preview.2 scripts/release/verify-runtime-host-multiarch.sh
> bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh
> bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
> ```
>
> Do **not** mark preview 2 readiness complete until the published `1.0.0-preview.2`
> tag is a multi-arch manifest list (both platforms) **and** the public-container
> sample smoke is green (`/ready` + commit + query against fresh Postgres) against
> that tag. The current public preview 1 image predates the SWR-G041 schema fix,
> so its live commit smoke is expected to fail until preview 2 is republished.

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

- [ ] The PR that introduces preview 2 ran `release-ghcr-image-preview` PR
  validation, which builds **both** `linux/amd64` and `linux/arm64`
  (Buildx + QEMU, `push: false`). A red arm64 leg is release-blocking — PR
  validation must not pass while only amd64 builds.
- [ ] The Dockerfile per-platform Wasmtime native-asset assertion passed for
  both legs (`libwasmtime.so` present at the publish root with an ELF
  `e_machine` matching the target arch: `0x3e` x86-64 / `0xb7` AArch64). A
  missing or wrong-arch native asset fails the build.
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

- [ ] **Per-platform Wasmtime native asset** is present and arch-correct in each
  image variant (the verification script checks `/app/libwasmtime.so` and its
  ELF `e_machine` for each platform; fails closed on missing or mismatch).
- [ ] The verification report records `Result: **PASS**` for the preview 2 tag.

### Fail-closed conditions

Treat any of the following as release-blocking — do **not** announce preview 2
as multi-arch until resolved:

- [ ] The manifest list is missing `linux/amd64` or `linux/arm64`.
- [ ] Either platform image fails to pull.
- [ ] A platform image is missing `/app/libwasmtime.so` or it is built for the
  wrong architecture.
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
