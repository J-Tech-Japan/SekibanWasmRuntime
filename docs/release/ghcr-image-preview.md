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

## Workflow

[`.github/workflows/release-ghcr-image-preview.yml`](../../.github/workflows/release-ghcr-image-preview.yml)

- **`pull_request`** (paths-filtered): builds the runtime host image from
  `src/runtime/Sekiban.Dcb.WasmRuntime.Host/Dockerfile` with `push: false` to
  validate the build. It never publishes.
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
`publish` job opts into `packages: write`, and that job runs only when all of
the following hold:

- `github.event_name == 'workflow_dispatch'`,
- the `push` input is `true`, and
- `github.repository == 'J-Tech-Japan/SekibanWasmRuntime'` (the canonical repo).

Pull-request builds and forks cannot publish.

## Tagging policy

- **Explicit preview tag** (e.g. `1.0.0-preview.1`): always set from the
  `image_tag` input. This is the stable, immutable reference for a given build.
- **Moving preview tag** (`preview`): optional, opt-in via `update_moving_tag`.
  It tracks the latest preview build for users who want "the current preview"
  without pinning a version. Pin the explicit tag for reproducible runs.

The first published target is a Linux OCI image. Multi-arch, Apple container,
and Windows container are out of scope for this preview path.

## Publish procedure

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
