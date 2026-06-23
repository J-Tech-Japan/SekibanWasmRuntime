# SekibanWasmRuntime

Runtime for WASM on Sekiban. Enables language-agnostic event projections by running projector logic inside a Wasmtime sandbox, supporting both C# and Rust WASM modules.

## License

SekibanWasmRuntime is an independent runtime module distributed under the
[Elastic License 2.0](LICENSE). See [NOTICE](NOTICE) for third-party
attributions and [CONTRIBUTING.md](CONTRIBUTING.md) for how contributions are
licensed.

Sekiban itself remains available under the Apache License 2.0. The ELv2 license
in this repository applies to SekibanWasmRuntime, and does not change the
license terms for the upstream Sekiban packages or submodule.

ELv2 allows users to use, modify, redistribute, and self-host
SekibanWasmRuntime, including for internal company use. It does not allow
providing SekibanWasmRuntime to third parties as a hosted service, managed
service, SaaS, or similar offering that gives users access to a substantial set
of its features, unless a separate commercial license has been agreed with
J-Tech Japan.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://docs.docker.com/get-docker/) (for Aspire emulators; also required for C# WASM build on macOS)
- [Rust toolchain](https://rustup.rs/) with `wasm32-wasip1` target (for Rust WASM build)

Install the Rust WASM target:

```bash
rustup target add wasm32-wasip1
```

## Project Structure

```
src/
├── lib/                               # Core WASM runtime libraries
│   ├── Sekiban.Dcb.WasmRuntime/       # Runtime abstractions
│   ├── Sekiban.Dcb.WasmRuntime.Remote/    # Remote runtime support
│   └── Sekiban.Dcb.WasmRuntime.Wasmtime/  # Wasmtime host integration
├── runtime/
│   └── Sekiban.Dcb.WasmRuntime.Host/  # Self-contained runtime host for containers
├── internalUsages/
│   ├── cs/                            # C# WASM example (Aspire + Blazor)
│   └── rust/                          # Rust WASM example (Aspire + Blazor)
├── wasm-projectors/
│   └── rust/                          # Rust WASM projector source
└── docker/
    └── sekiban-wasm-runtime/          # Docker compose stack for runtime + Postgres
```

See [src/internalUsages/README.md](src/internalUsages/README.md) for detailed architecture and comparison with the Sekiban reference implementation.

## NuGet Packages

The first public packages are preview packages versioned as `1.0.0-preview.*`.

| Package | Install when | Support tier |
| --- | --- | --- |
| `Sekiban.Dcb.WasmRuntime` | You need shared runtime contracts, projection abstractions, serialized command/query DTOs, and in-process client abstractions. | Primary preview package. |
| `Sekiban.Dcb.WasmRuntime.Remote` | Your app talks to a remote serialized Sekiban DCB runtime over HTTP. The generic remote executor belongs to the runtime boundary. | Primary preview package. |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | Your API/service process hosts WASM projections in-process with Wasmtime. | Preview package while the Wasmtime host policy and native asset packaging are finalized. |

Package metadata and package README content are maintained in
[`docs/nuget/package-readme.md`](docs/nuget/package-readme.md). The same ELv2
usage boundary described above applies to all SekibanWasmRuntime packages.
The Wasmtime preview package currently exposes a `Wasmtime` `14.0.0`
runtime/native asset dependency in its generated nuspec while compiling against
the managed Wasmtime source pinned in this repository.
Preview version, changelog, migration-note, and release evidence rules are
defined in
[`docs/release/versioning-and-changelog.md`](docs/release/versioning-and-changelog.md),
with human release history in [`CHANGELOG.md`](CHANGELOG.md).
NuGet package release readiness comes first; later source/repository
publication is staged with
[`docs/release/code-repository-release-checklist.md`](docs/release/code-repository-release-checklist.md).
See [`docs/quickstart.md`](docs/quickstart.md) for package-specific quickstart
paths.
See [`docs/compatibility/sekiban-as-a-service-boundary.md`](docs/compatibility/sekiban-as-a-service-boundary.md)
for the runtime-owned compatibility contract that downstream hosted-service
integrations can depend on.

## Quick Start

Start from the package that matches your runtime boundary:

- Core contracts only: install `Sekiban.Dcb.WasmRuntime`.
- HTTP client/runtime split: install `Sekiban.Dcb.WasmRuntime.Remote` in the
  client that calls serialized endpoints.
- In-process projection host: install `Sekiban.Dcb.WasmRuntime.Wasmtime` in the
  service that loads WASM projection modules.

```bash
dotnet add package Sekiban.Dcb.WasmRuntime --prerelease
dotnet add package Sekiban.Dcb.WasmRuntime.Remote --prerelease
dotnet add package Sekiban.Dcb.WasmRuntime.Wasmtime --prerelease
```

Most applications install only the package for the boundary they own. SaaS
credential helpers are outside this runtime package split.

### C# WASM Example

```bash
# 1. Build the C# WASM module
./build/scripts/build-csharp-wasm.sh

# 2. Start the Aspire-orchestrated system
dotnet run --project src/internalUsages/cs/SekibanWasm.Cs.AppHost
```

### Rust WASM Example

```bash
# 1. Build the Rust WASM module
./build/scripts/build-rust-wasm.sh

# 2. Start the Aspire-orchestrated system
dotnet run --project src/internalUsages/rust/SekibanWasm.Rust.AppHost
```

### Rust Generic Runtime Example

```bash
# 1. Build the Rust WASM module
./build/scripts/build-rust-wasm.sh

# 2. Start the generic runtime host + Rust ClientApi/Web stack
dotnet run --project src/internalUsages/rust/SekibanWasm.Rust.GenericAppHost
```

### Sample WASM Modules

Primary sample WASM modules are generated from source and are not tracked in git:

```bash
./scripts/build-samples-wasm.sh --primary
```

The primary supported samples are C# and Rust:

- C#: `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm`
- Rust: `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs`

Go, MoonBit, TypeScript, and Swift samples are experimental/reference until their
builds are promoted to the same CI-gated support tier.

For the full sample path, including prerequisites and the generic runtime host,
see [`docs/quickstart.md`](docs/quickstart.md).

### Generic Runtime Container

```bash
# 1. Put your Weather module here
cp src/internalUsages/cs/modules/csharp-weather.wasm docker/sekiban-wasm-runtime/modules/weather.wasm

# 2. Start runtime + postgres
cd docker/sekiban-wasm-runtime
docker compose up --build
```

See [docker/sekiban-wasm-runtime/README.md](docker/sekiban-wasm-runtime/README.md) for manifest details.

## Testing

```bash
# Run all tests
dotnet test src/SekibanWasmRuntime.ci.slnx

# Run E2E smoke tests (builds WASM modules + runs tests)
./build/scripts/run-e2e.sh
```

## Execution Models

SekibanWasmRuntime supports two execution models for WASM projections:

### Local (In-Process) Execution

The default mode. The API service hosts both Orleans grains and the Wasmtime projection runtime in the same process. Commands are committed and tag states are read locally via `ISerializedSekibanDcbExecutor`.

```
Client -> API Service (Orleans + WasmProjectionRuntime)
                |
                +-> PostgreSQL (event store)
```

Use `InProcSerializedDcbClient` for this mode. It delegates directly to `ISerializedSekibanDcbExecutor` without HTTP overhead.

### Remote (HTTP) Execution

For scenarios where the WASM client runs in a separate process (e.g., browser-side Blazor WASM). The client sends serialized commit requests over HTTP to the API service's serialized endpoints.

```
WASM Client -> HTTP -> API Service (/api/sekiban/serialized/*)
                            |
                            +-> ISerializedSekibanDcbExecutor
                            +-> PostgreSQL (event store)
```

Use `HttpSerializedDcbClient` for this mode. It calls the API service's serialized endpoints:
- `POST /api/sekiban/serialized/tag-state` - Get the current tag state
- `POST /api/sekiban/serialized/commit` - Commit serialized events with consistency tags

Both modes implement `ISerializedDcbClient`, so application code is transport-agnostic.

For higher-level application code, the repository now also provides common executor abstractions:

- C#: `ISekibanWasmExecutor`, `ISekibanCommandCommitRequestBuilder`, `ISerializedQueryClient`
- Rust: `sekiban-executor::HttpSekibanExecutor`, `SekibanExecutor`, `SekibanCommandCommitRequestBuilder`

## Submodules

This repository uses git submodules. Initialize them after cloning:

```bash
git submodule update --init --recursive
```
