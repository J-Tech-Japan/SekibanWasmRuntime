# Sekiban.Dcb.WasmRuntime.Aspire

Aspire AppHost hosting extension for the public
[Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime)
container. One `AddSekibanWasmRuntime` call wires the GHCR image, the runtime
manifest and wasm-module bind mounts, Postgres references, the environment
contract, an HTTP endpoint, and an optional health check — the same wiring the
public-container samples assemble by hand.

## Install

Install into your Aspire AppHost project with prerelease resolution enabled:

```bash
dotnet add package Sekiban.Dcb.WasmRuntime.Aspire --prerelease
```

The package targets Aspire AppHost projects only (it depends on
`Aspire.Hosting`); service and client projects use the runtime packages
instead — `Sekiban.Dcb.WasmRuntime`, `.Remote`, or `.Wasmtime` (see
[Related packages](#related-packages)).

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("pg");
// These database names become ConnectionStrings__SekibanDcb (event store) and
// ConnectionStrings__DcbMaterializedViewPostgres (materialized views) inside
// the runtime container — keep them exactly as shown.
var sekibanDb = postgres.AddDatabase("SekibanDcb");
var materializedViewDb = postgres.AddDatabase("DcbMaterializedViewPostgres");

builder.AddSekibanWasmRuntime("runtime", new SekibanWasmRuntimeOptions
{
    // Image defaults to ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3.
    ConfigDirectory = "/path/to/config",   // mounted read-only at /app/config
    ModulesDirectory = "/path/to/modules", // mounted read-only at /app/modules
    WasmModulePath = "/app/modules/my-projector.wasm",
    EventStoreDatabase = sekibanDb,
    MaterializedViewDatabase = materializedViewDb,
});

builder.Build().Run();
```

## Options

The options surface is intentionally minimal and mirrors the runtime container
contract:

| Option | Default | Maps to |
| --- | --- | --- |
| `Image` / `Tag` | public GHCR image, `1.0.0-preview.3` | container image |
| `ConfigDirectory` | – | read-only bind mount at `/app/config` |
| `ModulesDirectory` | – | read-only bind mount at `/app/modules` |
| `ManifestPath` | `/app/config/sekiban-manifest.json` | `SEKIBAN_MANIFEST_PATH` |
| `WasmModulePath` (required) | – | `WASM_MODULE_PATH` |
| `ProjectionMode` | `dual` | `SEKIBAN_PROJECTION_MODE` |
| `EventStoreDatabase` | – | `WithReference` → `ConnectionStrings__SekibanDcb` |
| `MaterializedViewDatabase` | – | `WithReference` → `ConnectionStrings__DcbMaterializedViewPostgres` |
| `EnvironmentVariables` | empty | extra/overriding env vars (win over the contract) |
| `EndpointName` / `TargetPort` / `HostPort` | `http` / 8080 / auto | HTTP endpoint (fixed `HostPort` publishes unproxied) |
| `ExternalHttpEndpoints` | `true` | dashboard-visible endpoint |
| `HealthCheckPath` | none | `WithHttpHealthCheck` (e.g. `/ready` strict readiness, `/health` liveness) |

The runtime connects to Postgres lazily and retries, so no `WaitFor` gate is
added on the databases — gating on Postgres health can stall headless runs and
is unnecessary.

A complete AppHost built on this package lives in the repository:
[PublicContainer.CsDecider sample](https://github.com/J-Tech-Japan/SekibanWasmRuntime/tree/main/src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider).

## Related packages

- `Sekiban.Dcb.WasmRuntime` — core abstractions and common types.
- `Sekiban.Dcb.WasmRuntime.Remote` — serialized HTTP client for the runtime.
- `Sekiban.Dcb.WasmRuntime.Wasmtime` — in-process Wasmtime execution.

## License

Elastic License 2.0 — see the packaged LICENSE file.
