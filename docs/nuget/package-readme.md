# SekibanWasmRuntime Preview Packages

SekibanWasmRuntime provides preview packages for running Sekiban DCB projection
logic through WebAssembly contracts.

These packages are published as `1.0.0-preview.*` while the public runtime
contract, package split, and Wasmtime host policy are finalized.

## Package Selection

Install `Sekiban.Dcb.WasmRuntime` when you need the shared runtime contracts,
projection abstractions, serialized command/query DTOs, and in-process client
abstractions.

Install `Sekiban.Dcb.WasmRuntime.Remote` when your application talks to a
remote serialized Sekiban DCB runtime over HTTP.

Install `Sekiban.Dcb.WasmRuntime.Wasmtime` when you host WASM projections
in-process with Wasmtime. This package is part of the initial preview matrix and
may carry preview Wasmtime dependency behavior while the host integration is
stabilized.

### Wasmtime Preview Caveat

`Sekiban.Dcb.WasmRuntime.Wasmtime` is a preview-only package in this release
line. It currently depends on the `Wasmtime` package version `35.0.0-dev` for
runtime/native assets while compiling against the managed Wasmtime source pinned
in this repository. Treat that dependency shape as provisional until the
Wasmtime host policy is finalized.

Package inspection is required before any publish. On macOS, the inspected
`1.0.0-preview.1` package includes `libwasmtime.dylib` under both `content/` and
`contentFiles/any/net10.0/`; Linux and Windows package candidates must be packed
and inspected on their release build environments so the expected native asset
for that platform is present. Do not treat this package as stable or
platform-complete without that inspection evidence.

## Install

Install the preview packages with prerelease resolution enabled:

```bash
dotnet add package Sekiban.Dcb.WasmRuntime --prerelease
dotnet add package Sekiban.Dcb.WasmRuntime.Remote --prerelease
dotnet add package Sekiban.Dcb.WasmRuntime.Wasmtime --prerelease
```

Most applications install only the package for their runtime boundary. Use the
core package for shared contracts, add the remote package in HTTP clients, and
add the Wasmtime package in API services that host projection modules
in-process.

## Minimal Usage

Core runtime consumers depend on `ISerializedDcbClient` so the application code
can use the same serialized DCB path for in-process and remote transports:

```csharp
using Sekiban.Dcb.WasmRuntime;

public sealed class ProjectionReader(ISerializedDcbClient client)
{
    public Task<ResultBoxes.ResultBox<Sekiban.Dcb.Tags.SerializableTagState>> ReadAsync(
        Sekiban.Dcb.Tags.TagStateId tagStateId) =>
        client.GetSerializableTagStateAsync(tagStateId);
}
```

Remote clients use `HttpSerializedDcbClient` with the serialized endpoint base
URL exposed by the API service:

```csharp
using System.Text.Json;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;

ISerializedDcbClient client = new HttpSerializedDcbClient(
    new HttpClient(),
    new SerializedDcbClientOptions { BaseUrl = "https://localhost:5001" },
    new JsonSerializerOptions(JsonSerializerDefaults.Web));
```

Wasmtime hosts register the in-process projection host and then enable the WASM
tag-state runtime after registering domain event types, JSON options, and a
`WasmProjectorRegistry`:

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

## License

SekibanWasmRuntime is licensed under Elastic License 2.0. You may use, modify,
redistribute, and self-host SekibanWasmRuntime, including for internal company
use. You may not provide SekibanWasmRuntime to third parties as a hosted service,
managed service, SaaS, or similar offering that gives users access to a
substantial set of its features, unless a separate commercial license has been
agreed with J-Tech Japan.

Sekiban itself remains available under Apache License 2.0. The license for this
repository does not change upstream Sekiban package or submodule terms.

## Repository

Source, issue tracking, and release notes are maintained at
https://github.com/J-Tech-Japan/SekibanWasmRuntime.
