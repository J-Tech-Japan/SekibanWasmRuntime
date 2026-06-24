# GHCR Preview Image — Runtime Host

The Sekiban WASM Runtime Host container is published to GitHub Container
Registry (GHCR) as a **preview** image:

```text
ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<tag>
```

Image publishing is intentionally **separate from NuGet publishing**. It uses
the built-in `GITHUB_TOKEN` with `packages: write` (scoped to the publish job
only) and never uses NuGet credentials, NuGet Trusted Publishing, or
`NUGET_API_KEY`.

## Separate release lanes

The NuGet packages and the runtime-host container image are **separate, independently
releasable release lanes**. They may share a compatible version string (e.g.
`1.0.0-preview.2`) but they do **not** share a GitHub Release object:

| Lane | Workflow | Trigger | Artifact |
| --- | --- | --- | --- |
| NuGet packages | [`release-nuget-preview.yml`](../../.github/workflows/release-nuget-preview.yml) | `release` event (GitHub Release) | `Sekiban.Dcb.WasmRuntime*` `.nupkg` |
| Runtime-host image | [`release-ghcr-image-preview.yml`](../../.github/workflows/release-ghcr-image-preview.yml) | `runtime-host-v*` git tag (or manual dispatch) | `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host` |

NuGet and the runtime-host image each have a **separate release lane**: the
runtime-host image lane is **never triggered by the NuGet `release` event**, and
this slice changes nothing about the NuGet workflow. The image tag prefix
`runtime-host-v*` follows the Sekiban artifact-family tag convention (e.g.
`dcbTemplates-v*`): the prefix identifies which artifact family is being released, so
NuGet and image releases can be cut on independent cadences.

## Workflow

[`.github/workflows/release-ghcr-image-preview.yml`](../../.github/workflows/release-ghcr-image-preview.yml)

- **`pull_request`** (paths-filtered): builds the runtime host image from
  `src/runtime/Sekiban.Dcb.WasmRuntime.Host/Dockerfile` for **both
  `linux/amd64` and `linux/arm64`** (Buildx + QEMU) with `push: false` to
  validate the build. It never publishes. Validating both legs means a change
  that breaks the arm64 build fails the PR instead of silently shipping an
  amd64-only image.
- **`push` to a `runtime-host-v*` tag**: the runtime-host image release lane. The
  image version is derived from the tag (`runtime-host-v1.0.0-preview.2` →
  `1.0.0-preview.2`) and the moving `preview` tag is updated.
- **`workflow_dispatch`**: manual run with inputs:
  - `image_tag` (required, default `1.0.0-preview.1`) — the explicit preview
    tag to build and, when pushing, publish.
  - `push` (boolean, default `false`) — when `true`, the publish job runs and
    pushes to GHCR. When `false`, the run is build-only validation.
  - `update_moving_tag` (boolean, default `false`) — when `true`, also updates
    the moving `preview` tag (`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:preview`)
    to this build, in addition to the explicit `image_tag`.

### Least privilege

The workflow declares top-level `permissions: contents: read`. Only the
`publish` job opts into `packages: write`, and that job runs only from the
canonical repository (`github.repository == 'J-Tech-Japan/SekibanWasmRuntime'`)
and only on the image release lane:

- a `runtime-host-v*` tag push, **or**
- a `workflow_dispatch` run with the `push` input set to `true`.

Pull-request builds and forks cannot publish.

## Tagging policy

Image tags are independent of the NuGet package version and follow these rules:

- **Immutable semver tag** (e.g. `1.0.0-preview.2`): the stable, reproducible
  reference for a given build. On the tag lane it is derived from the
  `runtime-host-v*` git tag; on dispatch it is the `image_tag` input. Never
  re-point an already-published immutable tag.
- **Moving `preview` tag**: tracks the latest preview build for users who want
  "the current preview" without pinning a version. Updated automatically on the
  tag lane, and opt-in via `update_moving_tag` on dispatch. Pin the immutable tag
  for reproducible runs.
- **No `latest` tag** is published for preview images (a moving `latest` would
  imply a stable line that does not exist yet); use the `preview` moving tag or a
  pinned immutable tag instead.

Published preview tags are **multi-arch Linux manifest lists** (see
[Platform support](#platform-support)). Apple container and Windows container
remain out of scope for this preview path.

## Platform support

Newly published preview tags are **manifest lists** that include both
`linux/amd64` and `linux/arm64`. Apple Silicon (arm64) developers can therefore
pull and run a current multi-arch tag **without** a platform override:

```bash
docker pull ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<tag>
```

The runtime host Dockerfile is built once per platform by Buildx, so each leg
downloads and packages its own Wasmtime native library
(`runtimes/linux-x64/native/libwasmtime.so` on amd64,
`runtimes/linux-arm64/native/libwasmtime.so` on arm64). The Dockerfile asserts
the native asset for the target platform is present and **fails the build** if it
is missing, so a manifest list never ships an image leg without its Wasmtime
runtime.

### Manifest inspection (release evidence)

The publish job inspects the pushed tag and fails closed if either platform is
absent from the manifest list. Reproduce that evidence with:

```bash
docker buildx imagetools inspect ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<tag>
# Expect Manifests entries for linux/amd64 and linux/arm64.

docker pull --platform linux/arm64 ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<tag>
docker pull --platform linux/amd64 ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<tag>
```

### Already-published amd64-only preview tags

Tags published before this slice (for example `1.0.0-preview.1`) are **amd64
only**. On Apple Silicon they fail with
`no matching manifest for linux/arm64/v8 in the manifest list entries` unless you
force the amd64 variant under emulation:

```bash
docker pull --platform linux/amd64 ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1
# or, for a single shell session:
export DOCKER_DEFAULT_PLATFORM=linux/amd64
```

Treat the `--platform linux/amd64` / `DOCKER_DEFAULT_PLATFORM=linux/amd64`
override strictly as a **workaround for those older amd64-only tags**, not as the
expected path for newly published multi-arch tags.

## Source-commit traceability and compatible baseline

Every published image records its source commit so an image can be traced back to
the exact repository state it was built from:

- `org.opencontainers.image.source` (set in the Dockerfile) →
  `https://github.com/J-Tech-Japan/SekibanWasmRuntime`.
- `org.opencontainers.image.revision` (set by the workflow) → the build commit
  SHA (`github.sha`).
- `org.opencontainers.image.version` (set by the workflow on publish) → the
  immutable image version.

Inspect them with `docker buildx imagetools inspect ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<tag>`
or `docker inspect`. **Compatible baseline:** an image built at a given commit is
compatible with the `Sekiban.Dcb.WasmRuntime*` preview packages produced from the
same source tree (matching `1.0.0-preview.*` line); when in doubt, trace the image
revision back to the commit and read its `Directory.Packages.props` /
[`docs/release/nuget-preview-release.md`](nuget-preview-release.md).

## Publish procedure

**Release lane (preferred) — push a `runtime-host-v*` tag:**

```bash
git tag runtime-host-v1.0.0-preview.2
git push origin runtime-host-v1.0.0-preview.2
```

The image lane builds and publishes `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.2`
and updates the moving `preview` tag. This is independent of any NuGet release.

**Manual dispatch (validation or ad-hoc publish):**

1. From the GitHub Actions UI, run **release-ghcr-image-preview** via
   *Run workflow* (`workflow_dispatch`).
2. Set `image_tag` to the preview version (e.g. `1.0.0-preview.1`).
3. To validate only, leave `push` unchecked — the build job runs without
   publishing.
4. To publish, set `push` to `true` (and optionally `update_moving_tag`).
5. Confirm the published package at
   `https://github.com/J-Tech-Japan/SekibanWasmRuntime/pkgs/container/sekiban-wasm-runtime-host`.

> Do not claim a GHCR publish succeeded unless the workflow actually ran with
> `push: true` and pushed the image. Adding the workflow alone makes publishing
> **ready for manual dispatch**; it does not publish an image by itself.

## Pull and run

See [`docker/sekiban-wasm-runtime/README.md`](../../docker/sekiban-wasm-runtime/README.md#published-image-ghcr-preview)
for `docker pull` / `docker run` usage of the published image and for switching
the compose `runtime` service from a local `build:` to a published `image:`.
