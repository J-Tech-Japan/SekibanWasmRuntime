# NuGet Consumer Smoke

Package version: `1.0.0-preview.0`
Package directory: `artifacts/nuget`
Consumer project: `artifacts/consumer-smoke-public-report/SekibanWasmRuntime.ConsumerSmoke/SekibanWasmRuntime.ConsumerSmoke.csproj`

## Referenced Packages

| Package | Version | Source |
| --- | --- | --- |
| `Sekiban.Dcb.WasmRuntime` | `1.0.0-preview.0` | `artifacts/nuget/Sekiban.Dcb.WasmRuntime.1.0.0-preview.0.nupkg` |
| `Sekiban.Dcb.WasmRuntime.Remote` | `1.0.0-preview.0` | `artifacts/nuget/Sekiban.Dcb.WasmRuntime.Remote.1.0.0-preview.0.nupkg` |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `1.0.0-preview.0` | `artifacts/nuget/Sekiban.Dcb.WasmRuntime.Wasmtime.1.0.0-preview.0.nupkg` |

## Commands

```bash
dotnet restore artifacts/consumer-smoke-public-report/SekibanWasmRuntime.ConsumerSmoke/SekibanWasmRuntime.ConsumerSmoke.csproj --configfile artifacts/consumer-smoke-public-report/SekibanWasmRuntime.ConsumerSmoke/NuGet.config --nologo
dotnet build artifacts/consumer-smoke-public-report/SekibanWasmRuntime.ConsumerSmoke/SekibanWasmRuntime.ConsumerSmoke.csproj -c Release --no-restore --nologo
```

## Result

Passed. The generated consumer project restored and built against the locally packed preview packages.

## Package Selection Guidance

See `docs/quickstart.md` and `docs/nuget/package-readme.md` for package selection guidance.

## Restore Output

```text
  Determining projects to restore...
  Restored /Users/tomohisa/dev/GitHub/SekibanWasmRuntime/artifacts/consumer-smoke-public-report/SekibanWasmRuntime.ConsumerSmoke/SekibanWasmRuntime.ConsumerSmoke.csproj (in 127 ms).
```

## Build Output

```text
  SekibanWasmRuntime.ConsumerSmoke -> /Users/tomohisa/dev/GitHub/SekibanWasmRuntime/artifacts/consumer-smoke-public-report/SekibanWasmRuntime.ConsumerSmoke/bin/Release/net10.0/SekibanWasmRuntime.ConsumerSmoke.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.37
```
