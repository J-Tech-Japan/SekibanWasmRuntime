# SekibanWasmRuntime Quickstart

SekibanWasmRuntime is a WASM-first runtime layer for Sekiban DCB projection
logic. The public preview packages are split by runtime boundary so applications
can depend on only the pieces they use.

## Choose a Package

| Package | Use it for | Typical project |
| --- | --- | --- |
| `Sekiban.Dcb.WasmRuntime` | Shared contracts, projection abstractions, serialized command/query DTOs, and in-process client abstractions. | Domain libraries, clients, and services that share runtime contracts. |
| `Sekiban.Dcb.WasmRuntime.Remote` | HTTP clients that call a remote serialized Sekiban DCB runtime. | Blazor/Web clients, service clients, or workers that execute through serialized endpoints. |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | In-process WASM projection hosting with Wasmtime. | API services or hosts that load projection modules from `.wasm` files. |

All three packages are currently preview packages versioned as
`1.0.0-preview.*`. The core and remote packages are primary preview packages.
The Wasmtime package is included in the preview matrix while native asset
packaging and host policy are finalized.

Install with prerelease resolution enabled:

```bash
dotnet add package Sekiban.Dcb.WasmRuntime --prerelease
dotnet add package Sekiban.Dcb.WasmRuntime.Remote --prerelease
dotnet add package Sekiban.Dcb.WasmRuntime.Wasmtime --prerelease
```

Most applications install only the package for their runtime boundary. SaaS
credential helpers are intentionally outside this runtime package split.

Release readiness also runs a local-package consumer smoke that restores and
builds a generated project against exact `1.0.0-preview.*` package versions from
the repository's locally packed `.nupkg` files. See
[`docs/release/nuget-preview-release.md`](release/nuget-preview-release.md) for
the release gate and generated evidence path.

## Core Runtime

Use `Sekiban.Dcb.WasmRuntime` when application code should depend on the
transport-neutral serialized DCB client contract:

```csharp
using Sekiban.Dcb.WasmRuntime;

public sealed class ProjectionReader(ISerializedDcbClient client)
{
    public Task<ResultBoxes.ResultBox<Sekiban.Dcb.Tags.SerializableTagState>> ReadAsync(
        Sekiban.Dcb.Tags.TagStateId tagStateId) =>
        client.GetSerializableTagStateAsync(tagStateId);
}
```

## Remote Runtime

Use `Sekiban.Dcb.WasmRuntime.Remote` in clients that call a remote serialized
runtime over HTTP:

```csharp
using System.Text.Json;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;

ISerializedDcbClient client = new HttpSerializedDcbClient(
    new HttpClient(),
    new SerializedDcbClientOptions { BaseUrl = "https://localhost:5001" },
    new JsonSerializerOptions(JsonSerializerDefaults.Web));
```

The generic remote executor is runtime-side. SaaS-specific credential helpers
belong outside this repository.

## Wasmtime Host

Use `Sekiban.Dcb.WasmRuntime.Wasmtime` in a service that hosts WASM projections
in-process:

```csharp
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

services.AddWasmtimeProjectionHost(options =>
{
    options.DefaultModulePath = "modules/projection.wasm";
});

services.AddWasmTagStateRuntime(options =>
{
    options.Mode = WasmRuntimeMode.Wasm;
});
```

Treat the Wasmtime package as preview-only until the host policy and
platform-native asset inspection are finalized. See
[`docs/nuget/package-readme.md`](nuget/package-readme.md) for the current
package caveat.

## Build Primary Samples

Primary sample WASM modules are built from source:

```bash
./scripts/build-samples-wasm.sh --primary
```

The primary supported sample paths are:

| Language | Path | Build helper |
| --- | --- | --- |
| C# | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm` | `build/scripts/build-sample-csharp-wasm.sh` |
| Rust | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs` | `scripts/build-samples-wasm.sh --primary` |

The internal usage examples also provide smaller C# and Rust runtime flows:

```bash
./build/scripts/build-csharp-wasm.sh
dotnet run --project src/internalUsages/cs/SekibanWasm.Cs.AppHost

./build/scripts/build-rust-wasm.sh
dotnet run --project src/internalUsages/rust/SekibanWasm.Rust.AppHost
```

Go, MoonBit, TypeScript, and Swift samples are experimental/reference until
their build and runtime paths are promoted to the same CI-gated support tier.

## Generic Runtime Container

The local runtime container is an OSS local backend host (not Sekiban Cloud).
You provide a WASM module (`WASM_MODULE_PATH`), a runtime manifest
(`SEKIBAN_MANIFEST_PATH`), and an external Event DB connection
(`ConnectionStrings__SekibanDcb`, Postgres by default), then run the serialized
Sekiban runtime over HTTP without hosting Orleans manually. Orleans clustering,
grain storage, and streams run in-memory inside the container while event
persistence stays external.

Build or copy a WASM module into the runtime container module directory, then
start the runtime and Postgres stack:

```bash
cp src/internalUsages/cs/modules/csharp-weather.wasm docker/sekiban-wasm-runtime/modules/weather.wasm

cd docker/sekiban-wasm-runtime
docker compose up --build
```

The container listens on port `8080` (the compose sample maps it to host port
`3000`) and exposes `GET /health`. For a single-container run, build the image
from the repository root and mount the manifest and module:

```bash
docker build -f src/runtime/Sekiban.Dcb.WasmRuntime.Host/Dockerfile -t sekiban-wasm-runtime .

docker run --rm -p 8080:8080 \
  -v "$PWD/docker/sekiban-wasm-runtime/config/sekiban-manifest.json:/app/config/sekiban-manifest.json:ro" \
  -v "$PWD/docker/sekiban-wasm-runtime/modules/weather.wasm:/app/modules/weather.wasm:ro" \
  -e SEKIBAN_MANIFEST_PATH=/app/config/sekiban-manifest.json \
  -e WASM_MODULE_PATH=/app/modules/weather.wasm \
  -e "ConnectionStrings__SekibanDcb=Host=host.docker.internal;Port=5432;Database=sekiban;Username=postgres;Password=postgres" \
  sekiban-wasm-runtime
```

Newly published GHCR preview tags are **multi-arch** manifest lists
(`linux/amd64` + `linux/arm64`), so Apple Silicon (arm64) users can
`docker pull ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<tag>` and run it
directly — no `DOCKER_DEFAULT_PLATFORM` override needed. The amd64 override
(`--platform linux/amd64` or `DOCKER_DEFAULT_PLATFORM=linux/amd64`) is only a
workaround for **older amd64-only preview tags** such as `1.0.0-preview.1`, which
fail on arm64 with `no matching manifest for linux/arm64/v8`. Verify a tag's
platforms with
`docker buildx imagetools inspect ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<tag>`.
Preview 2 (`1.0.0-preview.2`) is multi-arch but **shim-less** (it predates the
WASI preview2 shim fix), so `list-query` / materialized-view paths fail against
it. **Use `1.0.0-preview.3` — the verified, recommended public runtime tag.** It
is a published `linux/amd64` + `linux/arm64` manifest list
(digest `sha256:8bdebccd…`) whose images both carry `/app/libwasmtime.so` **and**
`/app/libwasmtime_preview2_shim.so`, the moving `preview` tag points at the same
digest, and the public-container sample smoke passes end-to-end against it
(`/health`, schema-aware `/ready`, command commit, tag-state read, `list-query`,
and Materialized View catch-up). See the verification evidence in
[`docs/release/runtime-host-preview-3-release-verification.md`](release/runtime-host-preview-3-release-verification.md).

The GHCR runtime-host **container** tag and the **NuGet** package versions are
independent lanes and move on their own cadence: the latest verified runtime-host
container tag is **`1.0.0-preview.3`**, while the latest public NuGet packages
(`Sekiban.Dcb.WasmRuntime`, `…Remote`, `…Wasmtime`) are **`1.0.0-preview.1`**. Do
not assume the two share a version number.

Rust support now has two lanes. Existing repo-development samples continue to
use local path dependencies under `src/wasm-projectors/rust`, while the
crates.io consumer sample uses the published Rust crates at exact `=0.1.0`
versions:

```toml
sekiban-core = "=0.1.0"
sekiban-derive = "=0.1.0"
sekiban-wasm = "=0.1.0"
sekiban-mv = "=0.1.0"
sekiban-executor = "=0.1.0"
```

The public Rust distribution crates are exactly those five
(`sekiban-core`, `sekiban-derive`, `sekiban-wasm`, `sekiban-mv`,
`sekiban-executor`). The other workspace crates under `src/wasm-projectors/rust`
(`sekiban-wasm-domain`, `sekiban-wasm-projector`) are internal sample/reference
crates marked `publish = false`. Shared package metadata (authors, license,
homepage, repository, keywords) is centralized in `[workspace.package]` and
inherited by each public crate, mirroring how NuGet centralizes metadata in
`Directory.Build.props`. See
[`docs/release/rust-crate-metadata-policy.md`](release/rust-crate-metadata-policy.md)
for the full public/internal crate set and the Cargo/NuGet metadata policy.

Use
[`src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider`](../src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider)
when checking the external Rust package boundary. Its verification script runs
`cargo metadata`, `cargo check --workspace`, and a guard that fails if the sample
uses repository-local Sekiban crate paths or the unpublished
`sekiban-wasm-domain` helper. See
[`docs/release/rust-crates-io-consumer-sample.md`](release/rust-crates-io-consumer-sample.md)
for the release evidence and commands.

That same sample now has a full end-to-end smoke that runs the published-crate
client against the **public GHCR runtime container** through a sample-owned
Aspire AppHost:

```bash
env -u SAMPLE_RUNTIME_IMAGE_TAG \
  bash src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/scripts/smoke.sh
```

It validates command execution, tag-state readback, in-memory projection
queries, and materialized-view catch-up using only crates.io `=0.1.0` Sekiban
dependencies and the public runtime image (default `1.0.0-preview.3`, override
with `SAMPLE_RUNTIME_IMAGE_TAG`). This is the published-package counterpart to
the local path-based `PublicContainer.RsDecider` sample below: same public
runtime container, but consuming the published crates instead of repository-local
Rust paths. The smoke skips gracefully when Docker, the .NET SDK, cargo, or the
`wasm32-wasip1` target are unavailable.

The Go equivalent is
[`src/samples/Sekiban.Dcb.WasmRuntime.GoModule.GoDecider`](../src/samples/Sekiban.Dcb.WasmRuntime.GoModule.GoDecider):
its committed `go.mod` requires the published subdirectory module
`github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go` with no replace
directives (guarded by its `verify-no-local-sekiban-paths.sh`), and its smoke
runs the same four checks against the public runtime container. Until the
`src/lib/sekiban-go/v0.1.0` tag is published, run the clearly-labeled
pre-publish dry-run (`smoke.sh --local-module`, via the `go.work` overlay); the
replace-free published-module run (`smoke.sh`, `GOWORK=off`) is the recorded
follow-up once the tag exists. See the sample README for the two-stage
verification.

The Swift equivalent is
[`src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider`](../src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider):
its committed `Package.swift` depends on the public
`github.com/J-Tech-Japan/sekiban-swift` mirror at exact 0.1.0 with no
path-based references (guarded), and its smoke runs the same four checks
against the public runtime container. Until the mirror is public, run the
clearly-labeled pre-publish dry-run (`smoke.sh --local-package`, redirecting
the URL to the staged mirror tree via SwiftPM dependency mirroring); the
mirror-resolved run (`smoke.sh`) is the recorded follow-up. Its
`linux-build-check.sh` records whether the package builds with Swift in a
Linux container (see `docs/release/swift-sdk-release-lane.md`).

See [`docker/sekiban-wasm-runtime/README.md`](../docker/sekiban-wasm-runtime/README.md)
for the public local runtime container contract: provided/non-goal behavior,
ports, volumes, required and optional environment variables, storage-provider
configuration, and container-engine support (Docker first-class; Podman OCI
compatibility target; Apple container and Windows container as future targets).

### Public-consumer sample (NuGet + GHCR + Aspire)

For an end-to-end proof that consumes only the published artifacts — public NuGet
DCB packages, the public GHCR runtime image, a WASM-compiled Decider domain, and
Postgres via Aspire — see
[`docs/samples/public-container-cs-decider.md`](samples/public-container-cs-decider.md)
and the sample under
[`src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider`](../src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider).

The Rust local-development equivalent is
[`docs/samples/public-container-rs-decider.md`](samples/public-container-rs-decider.md)
and
[`src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider`](../src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider).
It builds the Rust Decider WASM module from repo-local Rust crates, stages the
module and manifest under `artifacts/samples/public-container-rs-decider`, runs
the same public GHCR runtime host through Aspire `AddContainer`, and uses a
typed Rust `RemoteSekibanExecutor` smoke client. crates.io publication remains
deferred until the protected GitHub Actions first-publish gate is approved and
executed, so the Rust sample intentionally uses path dependencies for now.

The sample also demonstrates a **Materialized View** through the same public
artifacts: the WASM module exports `mv_metadata` / `mv_initialize` /
`mv_apply_event`, the manifest declares `materializedViews`, the AppHost wires a
second Postgres `DcbMaterializedViewPostgres` and `SEKIBAN_PROJECTION_MODE=dual`,
and the smoke reads the caught-up view **directly from Postgres** (MV reads are
caller-owned — the runtime host has no MV read API). Live MV verification needs a
runtime image carrying the WASI preview2 shim; see
[`docs/release/runtime-host-mv-public-artifact-evidence.md`](release/runtime-host-mv-public-artifact-evidence.md).
