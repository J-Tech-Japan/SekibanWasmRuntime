# Design: Switch SekibanWasm.ApiService Projections to C# WASM

This document describes how to change `src/internalUsage/SekibanWasm.ApiService` from the current *native* projection runtime to the *C# WASM* projection runtime produced by `src/internalUsage/SekibanWasm.Wasm`.

## Goal

- Run projection logic (apply events, execute queries, serialize/restore projection state) inside a WASM module built from C#.
- Keep the rest of the service (HTTP API, storage, Orleans wiring, etc.) in .NET as-is.

## Non-goals (for the first milestone)

- Do not attempt a “full WASM domain” where all domain types/commands are discovered from WASM at runtime (that’s closer to the POC’s `WasmServer` approach).
- Do not require a separate “WasmServer” process.
- Do not add Playwright; start with a smoke test (curl-based) similar in spirit to the POC’s `e2e-aspire-playwright.sh`.

## Current State (as of this repo)

- `src/internalUsage/SekibanWasm.ApiService/Program.cs` registers:
  - `builder.Services.AddSekibanDcbNativeRuntime();`
  - This maps `IProjectionRuntime` to `NativeProjectionRuntime` (see `submodules/Sekiban/.../SekibanDcbNativeRuntimeExtensions.cs`).
- This repo already contains:
  - A WASM projection runtime: `src/lib/Sekiban.Dcb.WasmRuntime/WasmProjectionRuntime.cs`
  - A Wasmtime-backed host: `src/lib/Sekiban.Dcb.WasmRuntime.Wasmtime/*`
  - A C# WASM module project: `src/internalUsage/SekibanWasm.Wasm`
    - Exports ABI functions like `create_instance`, `apply_event`, `execute_query`, `serialize_state`, etc. (`src/internalUsage/SekibanWasm.Wasm/WasmExports.cs`)

## Architecture Overview

### Runtime layering

1. **Sekiban DCB** calls `IProjectionRuntime` for projection work.
2. Replace the projection runtime with **`WasmProjectionRuntime`**.
3. `WasmProjectionRuntime` delegates to an **`IPrimitiveProjectionHost`**.
4. Use **`WasmtimePrimitiveProjectionHost`** as the host implementation.
5. `WasmtimePrimitiveProjectionHost` loads a `.wasm` module and creates instances that call into exports in `WasmExports`.

### What becomes “C# WASM”

Only the projection computations and query execution:

- `apply_event` / `apply_events_batch`
- `execute_query` / `execute_list_query`
- `serialize_state` / `restore_state`

### What stays “native .NET”

- HTTP endpoints (Minimal API)
- storage and event store (Postgres via `Sekiban.Dcb.Postgres`)
- orchestration (Aspire AppHost)
- any Orleans/grain hosting you keep

## Key Design Decisions

### 1) How the WASM module selects “which projector”

`WasmtimePrimitiveProjectionInstance` passes a string (currently `projectorName`) to the WASM export `create_instance`.

In the current C# WASM module, `ResolveProjectorKind()` uses substring matching on that string:

- contains `weatherforecastprojector` => “tag projector”
- contains `weatherforecast` or `weather` => “list projector”

So the simplest integration is:

- Keep projector names/types as-is (they already contain `WeatherForecastProjector` etc.).
- Ensure the caller uses projector names that match those rules.

If we need stronger typing later, we should switch from substring matching to an explicit mapping table embedded in the module (or generated manifest).

### 2) Module distribution and versioning

`WasmModuleRef` carries:

- `ProjectorName`
- `ModulePath`
- `AbiKind`
- `ModuleVersion`
- `ProjectorVersion`

First milestone: ship a single local `.wasm` file built from `SekibanWasm.Wasm` and point all projector names to that module path.

Later: support multiple modules and/or per-projector modules.

## Implementation Plan (DI wiring)

### A) Build the C# WASM module

Produce a `.wasm` artifact from `SekibanWasm.Wasm`:

- command shape (exact flags may evolve):
  - `dotnet publish src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj -c Release -r wasi-wasm`
- choose a stable output path for the produced `.wasm` (recommended):
  - `artifacts/wasm/sekibanwasm.wasm`

Notes:

- The project currently targets `net10.0` and `wasi-wasm`.
- Avoid package-based ILCompiler bits that are not reliably available; prefer SDK/workload-provided toolchain.

### B) Replace the projection runtime in ApiService

In `src/internalUsage/SekibanWasm.ApiService/Program.cs`:

1. Remove (or do not call) `AddSekibanDcbNativeRuntime()` because it sets `IProjectionRuntime` to native.
2. Register:
  - `WasmProjectorRegistry`
  - `WasmtimeRuntime`, `WasmtimeModuleCache`
  - `IPrimitiveProjectionHost` = `WasmtimePrimitiveProjectionHost`
  - `IProjectionRuntime` = `WasmProjectionRuntime`

Pseudo-registration:

```csharp
builder.Services.AddSingleton<WasmProjectorRegistry>(sp =>
{
    var reg = new WasmProjectorRegistry();
    reg.Register(new WasmModuleRef(
        ProjectorName: "WeatherForecastProjector",
        ModulePath: "<path-to-wasm>",
        AbiKind: "cabi-preview1", // or repo-defined string
        ModuleVersion: "<git-sha-or-semver>",
        ProjectorVersion: "<domain-version>"
    ));
    // Optionally register additional projectors.
    return reg;
});

builder.Services.AddWasmtimeProjectionHost(opt =>
{
    opt.DefaultModulePath = "<path-to-wasm>";
    // opt.ProjectorModulePaths["WeatherForecastProjector"] = "<path-to-wasm>";
});

builder.Services.AddSingleton<IProjectionRuntime, WasmProjectionRuntime>();
```

Important nuance:

- `WasmProjectionRuntime` currently uses `IPrimitiveProjectionHost.CreateInstance(projectorName)` and
  `WasmtimePrimitiveProjectionHost` resolves module paths via `WasmtimeHostOptions`.
- `WasmProjectorRegistry.ModulePath` is not currently used by the host.

Two options:

- Short-term: configure `WasmtimeHostOptions` so `ResolveModulePath(projectorName)` returns the correct module.
- Better: refactor host/runtime so module path is resolved from `WasmProjectorRegistry` (single source of truth).

### C) Keep event runtime native (optional but recommended)

`AddSekibanDcbNativeRuntime()` also registers `IEventRuntime`, `ITagProjectionRuntime`, etc.

For the “projections in WASM” path:

- Keep a native `IEventRuntime` (because event persistence is still .NET).
- Override only `IProjectionRuntime` with `WasmProjectionRuntime`.

If overriding `IProjectionRuntime` alone isn’t sufficient (depends on Sekiban DCB internals), then create a dedicated extension method:

- `AddSekibanDcbWasmProjectionRuntime(...)`

that registers the minimal set of runtime abstractions needed.

## Functional Gaps to Address (ApiService correctness)

### 1) `ISekibanExecutor` wiring

The ApiService currently uses `ISekibanExecutor` in endpoints but does not clearly register it.

Before “C# WASM projections” can be meaningfully exercised end-to-end, we need a concrete executor:

- Orleans-backed executor (like the DCB internal usages)
- or an in-memory/local executor for a dev-only mode
- or a remote executor (POC style) if you choose a separate engine

The POC’s `ClientApi` demonstrates a pattern of registering a remote executor as `ISekibanExecutor` to keep the API surface consistent.

### 2) Query routing to projector

If Sekiban DCB needs a mapping from query type -> projector name, use:

- `WasmProjectorRegistry.MapQueryToProjector(queryTypeName, projectorName)`

If not required by core runtime, keep it for future multi-module setups.

## Configuration

Add an appsetting section (or environment variables) for:

- `WASM_MODULE_PATH` (or `Wasm:DefaultModulePath`)
- optional per-projector overrides

Example:

```json
{
  "Wasm": {
    "DefaultModulePath": "artifacts/wasm/sekibanwasm.wasm",
    "Projectors": {
      "WeatherForecastProjector": "artifacts/wasm/sekibanwasm.wasm"
    }
  }
}
```

Then bind it into `WasmtimeHostOptions`.

## Testing Strategy

### 1) Build-level

- `dotnet build src/SekibanWasmRuntime.slnx -c Release`

### 2) WASM module build

- `dotnet publish` the WASM project
- verify the `.wasm` artifact exists

### 3) Smoke E2E (no browser)

Add a script similar to the POC’s approach:

- start the Aspire AppHost
- wait for the API endpoint
- issue a basic GET/POST to validate the service does not throw

This repo already has a starter script shape at `scripts/e2e-aspire-smoke.sh` that can be extended to:

- trigger a command (POST)
- then query the projected state (GET)

## Risks / Open Questions

- What exact `projectorName` strings does Sekiban DCB pass to `IProjectionRuntime` for:
  - tag projections vs multi projections
  - queries
  This must match what the WASM module expects in `create_instance`.
- How to version and locate the `.wasm` module in CI (artifact vs checked-in file).
- Whether `ITagProjectionRuntime` also needs a WASM implementation (depends on DCB runtime behavior).
- Whether we want to adopt the POC “WasmServer” split (remote execution) or keep “in-process Wasmtime”.

## Guide-vs-Implementation Differences

The original guide (`IMPLEMENTATION_GUIDE.md`) proposed creating separate projects
(`DcbRuntime.WasmOnly.ApiService`, etc.) for each runtime mode. The actual implementation
diverges from this approach for the following reasons:

| Guide Proposal | Actual Implementation | Reason |
|---|---|---|
| Separate `DcbRuntime.WasmOnly.ApiService` project | Runtime switching via `SEKIBAN_PROJECTION_RUNTIME` env var in `SekibanWasm.ApiService` | Avoids project proliferation; a single entry-point with config-driven behaviour is simpler to maintain and deploy |
| `AddSekibanDcbNativeRuntime()` removed in WASM mode | `AddSekibanDcbNativeRuntime()` always called; WASM/hybrid/remote overrides `IProjectionRuntime` afterward | Native runtime also registers `IEventRuntime` and `ITagProjectionRuntime`, which remain needed regardless of projection mode |
| `BuildServiceProvider()` for hybrid runtime resolution | Factory delegate pattern resolving NativeProjectionRuntime by concrete type | Eliminates `ASP0000` warning; avoids creating a second service provider and circular dependency |
| `PublishTrimmed=true` in WASM csproj | `PublishTrimmed=false` + `IlcTrimMetadata=false` | Required to avoid IL1034 errors with the current NativeAOT-WASI toolchain for library outputs |
| No `global.json` / `Directory.Packages.props` | Both files added at repository root | Ensures SDK version pinning and central package version management for reproducible builds |
| WASM build not in CI | CI runs both `build-csharp-wasm.sh` and `build-rust-wasm.sh` between Build and Test | Catches WASM build regressions automatically |

## Reference: POC patterns

If you need an example of port pinning and E2E orchestration, see:

- `/Users/tomohisa/dev/GitHub/SekibanAsAService/src/poc/scripts/e2e-aspire-playwright.sh`
- `/Users/tomohisa/dev/GitHub/SekibanAsAService/src/poc/SekibanWasmPoc.AppHost/Program.cs` (E2E endpoint port override)

## GUIDE5: Final Operational Rules

This section consolidates the operational rules established by GUIDE1-4 into a single reference
for day-to-day use. GUIDE3 below documents _why_ these decisions were made; this section
documents _what_ to do.

### Build commands

Execute in the following order for a full pipeline:

```bash
dotnet restore src/SekibanWasmRuntime.ci.slnx
dotnet build src/SekibanWasmRuntime.ci.slnx -c Release
./build/scripts/build-csharp-wasm.sh
./build/scripts/build-rust-wasm.sh
dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build
dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget
```

### NuGet configuration rules

| File | Used by | Content |
|------|---------|---------|
| `NuGet.config` | `dotnet restore`, `dotnet build`, `dotnet test`, `dotnet pack` | `nuget.org` only (single source eliminates NU1507) |
| `NuGet.wasm.config` | `build-csharp-wasm.sh` via `--configfile` | `nuget.org` + `dotnet10` + `dotnet-experimental` with `packageSourceMapping` for ILCompiler |

Never add extra feeds to `NuGet.config`. ILCompiler feeds belong exclusively in `NuGet.wasm.config`.

### Docker requirements

Docker is required on non-Linux hosts to build C# WASM modules. `build-csharp-wasm.sh`
automatically detects the host OS and switches between native (Linux) and Docker mode.

- Docker image: `mcr.microsoft.com/dotnet/sdk:10.0` (GA tag)
- Platform: `--platform linux/amd64`
- WASI SDK v29 is installed inside the container

### SDK version policy

- `global.json` pins SDK to `10.0.100` with `rollForward: "latestFeature"`
- This selects the latest SDK within the `10.0.1xx` feature band
- `build-csharp-wasm.sh` validates that the Docker container has a matching SDK (`10.0.1` prefix)

### Solution file separation

| File | Purpose | Contains WASM project |
|------|---------|-----------------------|
| `src/SekibanWasmRuntime.ci.slnx` | CI: restore, build, test, pack | No (`SekibanWasm.Wasm` excluded) |
| `src/SekibanWasmRuntime.slnx` | Development: all projects | Yes |

`SekibanWasm.Wasm` is excluded from `ci.slnx` because it requires ILCompiler feeds
that would re-introduce NU1507 in the normal restore path.

### Validation scripts

Run these to verify build infrastructure without needing Docker or submodules:

```bash
./build/scripts/tests/test-build-csharp-wasm.sh      # script structure validation
./build/scripts/tests/test-nuget-source-mapping.sh    # NuGet.config is nuget.org only
./build/scripts/tests/test-nuget-wasm-config.sh       # NuGet.wasm.config has ILCompiler feeds
./build/scripts/tests/test-csproj-ilcompiler.sh       # ILCompiler package conditions
```

## GUIDE3: Build Reproducibility Policy

### Linux-only toolchain via Docker fallback

`runtime.osx-arm64.microsoft.dotnet.ilcompiler.llvm` is unstable in the .NET 10 preview feed
and frequently fails to resolve (`NU1101`). To guarantee reproducible builds across macOS
and Linux CI, `build-csharp-wasm.sh` now uses a single toolchain strategy:

- **Linux (CI):** `dotnet publish` runs natively — the `runtime.linux-x64` ILCompiler package
  is always available.
- **Non-Linux (local dev):** The script runs `dotnet publish` inside a
  `mcr.microsoft.com/dotnet/sdk:10.0` Docker container with WASI SDK v29 installed
  in-container. This matches CI exactly.

The macOS ILCompiler runtime package reference in `SekibanWasm.Wasm.csproj` is retained
behind an opt-in property (`EnableMacIlCompilerRuntime=true`) for future use, but is
disabled by default.

### NuGet configuration separation

NuGet configuration is split into two files to eliminate `NU1507` warnings during normal
restore while retaining ILCompiler feed access for WASM builds:

- **`NuGet.config`** — Used by `dotnet restore`, `dotnet build`, `dotnet test`, `dotnet pack`.
  Contains only the `nuget.org` feed. No `packageSourceMapping` is needed because there is
  only a single source, which eliminates `NU1507`.
- **`NuGet.wasm.config`** — Used exclusively by `build-csharp-wasm.sh` via `--configfile`.
  Contains `nuget.org`, `dotnet10`, and `dotnet-experimental` feeds with
  `packageSourceMapping` to route ILCompiler packages to the correct feed.

### Docker image pinning

`build-csharp-wasm.sh` uses `mcr.microsoft.com/dotnet/sdk:10.0` (GA tag, not preview).
The script validates that the container's SDK version starts with `10.0.1` (matching
the `10.0.1xx` feature band specified by `global.json`'s `rollForward: "latestFeature"`
policy). If no matching SDK is found, the build fails immediately with a clear error.

### Local prerequisites

- Docker is required on non-Linux hosts to run `build-csharp-wasm.sh`.
- WASI SDK is not needed on the host; it is installed inside the Docker container.

