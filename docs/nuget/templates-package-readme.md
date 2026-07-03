# Sekiban.Dcb.WasmRuntime.Templates

`dotnet new` templates for the
[Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime).

## Install

```bash
dotnet new install Sekiban.Dcb.WasmRuntime.Templates
```

## sekiban-wasm-decider

Generates an Aspire solution that hosts the **public runtime container**
(`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`) with a
Postgres event store:

- a Decider-pattern weather domain on the public `Sekiban.Dcb.WithoutResult`
  package (events, commands, tag projector, multi-projection, queries);
- a NativeAOT-LLVM `wasi-wasm` reactor project compiled to the runtime's WASM
  projector module by the included `scripts/build-wasm.sh`;
- an Aspire AppHost wiring the container, bind mounts, environment contract,
  and Postgres references through `Sekiban.Dcb.WasmRuntime.Aspire`'s single
  `AddSekibanWasmRuntime` call;
- an end-to-end smoke script (health/ready, command commit, tag-state
  readback, list-query) and an optional xUnit test project.

```bash
dotnet new sekiban-wasm-decider -n MyWeather
cd MyWeather
bash scripts/build-wasm.sh
dotnet run --project MyWeather.AppHost
```

Options:

| Option | Default | Effect |
| --- | --- | --- |
| `-n <name>` | directory name | Replaces the `SekibanDcbDecider` source name across projects, namespaces, and scripts. |
| `--IncludeTests` | `true` | Include the xUnit domain test project. |

Prerequisites: .NET 10 SDK and Docker (runtime container; also the wasm build
on macOS/Windows).

## License

Elastic License 2.0 — see the packaged LICENSE file.
