# NuGet Package Inspection

Package version: `1.0.0-preview.1`
Package directory: `artifacts/issue-143-nuget`

| Package | Id | Version | Bytes | Readme | License | Repository |
| --- | --- | --- | ---: | --- | --- | --- |
| `Sekiban.Dcb.WasmRuntime.1.0.0-preview.1.nupkg` | `Sekiban.Dcb.WasmRuntime` | `1.0.0-preview.1` | `59171` | `README.md` | `LICENSE` | `https://github.com/J-Tech-Japan/SekibanWasmRuntime` |
| `Sekiban.Dcb.WasmRuntime.Remote.1.0.0-preview.1.nupkg` | `Sekiban.Dcb.WasmRuntime.Remote` | `1.0.0-preview.1` | `31788` | `README.md` | `LICENSE` | `https://github.com/J-Tech-Japan/SekibanWasmRuntime` |
| `Sekiban.Dcb.WasmRuntime.Wasmtime.1.0.0-preview.1.nupkg` | `Sekiban.Dcb.WasmRuntime.Wasmtime` | `1.0.0-preview.1` | `13343450` | `README.md` | `LICENSE` | `https://github.com/J-Tech-Japan/SekibanWasmRuntime` |

## Dependencies

| Package Id | Target Framework | Dependency | Version | Exclude |
| --- | --- | --- | --- | --- |
| `Sekiban.Dcb.WasmRuntime` | `net10.0` | `Sekiban.Dcb.Core` | `10.2.2` | `Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime` | `net10.0` | `Sekiban.Dcb.Core.Model` | `10.2.2` | `Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime` | `net10.0` | `Sekiban.Dcb.Orleans.Core` | `10.2.2` | `Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime.Remote` | `net10.0` | `Sekiban.Dcb.WasmRuntime` | `1.0.0-preview.1` | `Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime.Remote` | `net10.0` | `Sekiban.Dcb.WithoutResult` | `10.2.2` | `Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime.Remote` | `net10.0` | `Sekiban.Dcb.WithoutResult.Model` | `10.2.2` | `Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `net10.0` | `Wasmtime` | `14.0.0` | `Compile,Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `net10.0` | `Sekiban.Dcb.WasmRuntime` | `1.0.0-preview.1` | `Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `net10.0` | `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.3` | `Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `net10.0` | `Microsoft.Extensions.Logging.Abstractions` | `10.0.3` | `Build,Analyzers` |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `net10.0` | `Microsoft.Extensions.Options` | `10.0.3` | `Build,Analyzers` |

## Native Content Assets

| Package Id | Asset |
| --- | --- |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `content/libwasmtime.dylib` |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `contentFiles/any/net10.0/libwasmtime.dylib` |

## Result

Passed.
