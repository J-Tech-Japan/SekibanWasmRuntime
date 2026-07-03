# SekibanDcbDecider

A Sekiban WASM Runtime solution generated from the `sekiban-wasm-decider`
template: a Decider-pattern weather domain compiled to a WASM projector
module, hosted by the **public runtime container**
(`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`) with a Postgres event store
through Aspire.

## Layout

```text
SekibanDcbDecider.Domain/        Decider domain (events, commands, tag projector,
                                 multi-projection, queries) on Sekiban.Dcb.WithoutResult.
SekibanDcbDecider.Wasm/          NativeAOT-LLVM wasi-wasm reactor exposing the runtime
                                 ABI for the domain (built by scripts/build-wasm.sh,
                                 not by a regular dotnet build).
SekibanDcbDecider.AppHost/       Aspire AppHost: AddSekibanWasmRuntime wires the public
                                 container + Postgres (SekibanDcb event store and
                                 DcbMaterializedViewPostgres for materialized views).
SekibanDcbDecider.Domain.Tests/  xUnit tests for the domain (generated when
                                 IncludeTests=true, the default).
scripts/build-wasm.sh            Builds modules/SekibanDcbDecider.wasm + config/sekiban-manifest.json.
scripts/smoke.sh                 End-to-end smoke: health/ready, commit, tag-state
                                 readback, list-query against the running container.
```

## Run

```bash
# 1. Build the WASM module + runtime manifest (Docker on macOS/Windows,
#    native WASI SDK on Linux):
bash scripts/build-wasm.sh

# 2. Start everything (Postgres + public runtime container):
dotnet run --project SekibanDcbDecider.AppHost

# or run the end-to-end smoke instead (starts the AppHost itself):
bash scripts/smoke.sh
```

The runtime image tag defaults to `1.0.0-preview.3`; override with
`SAMPLE_RUNTIME_IMAGE_TAG`.

## Build and test

```bash
dotnet build SekibanDcbDecider.Domain
dotnet build SekibanDcbDecider.AppHost
dotnet test SekibanDcbDecider.Domain.Tests   # when generated with IncludeTests=true
```

The `SekibanDcbDecider.Wasm` project compiles to `wasi-wasm` with
NativeAOT-LLVM and is deliberately built by `scripts/build-wasm.sh` (which
provisions the WASI SDK via Docker on non-Linux hosts) rather than by a plain
`dotnet build`.

Prerequisites: .NET 10 SDK, Docker (for the runtime container and, on
macOS/Windows, the wasm build).
