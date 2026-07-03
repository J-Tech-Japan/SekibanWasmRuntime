# `sekiban-wasm-decider` template

The `Sekiban.Dcb.WasmRuntime.Templates` package ships a `dotnet new` template
that generates a complete Aspire solution running a Decider-pattern domain on
the **public Sekiban WASM runtime container**
(`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`).

- Template short name: `sekiban-wasm-decider`
- Template identity: `Sekiban.Dcb.WasmRuntime.Aspire.Decider`
- Source name (replaced by `-n`): `SekibanDcbDecider`
- Package source tree: `templates/Sekiban.Dcb.WasmRuntime.Templates/`

## Install

```bash
# From NuGet (after the templates package is published):
dotnet new install Sekiban.Dcb.WasmRuntime.Templates

# From a locally packed nupkg (pre-publish / development):
dotnet pack templates/Sekiban.Dcb.WasmRuntime.Templates/Sekiban.Dcb.WasmRuntime.Templates.csproj \
  -c Release -o artifacts/packages
dotnet new install artifacts/packages/Sekiban.Dcb.WasmRuntime.Templates.*.nupkg
```

## Generate

```bash
dotnet new sekiban-wasm-decider -n MyWeather
```

| Option | Default | Effect |
| --- | --- | --- |
| `-n <name>` | output directory name | Replaces `SekibanDcbDecider` across project names, namespaces, scripts, and the wasm module name. |
| `--IncludeTests` | `true` | Generate the xUnit domain test project (`<name>.Domain.Tests`). Pass `--IncludeTests false` to omit it. |

## Generated layout

```text
MyWeather/
├── MyWeather.Domain/        Decider domain (events, commands, tag projector,
│                            multi-projection, queries) on Sekiban.Dcb.WithoutResult.
├── MyWeather.Wasm/          NativeAOT-LLVM wasi-wasm reactor exposing the runtime ABI.
├── MyWeather.AppHost/       Aspire AppHost; AddSekibanWasmRuntime (from the
│                            Sekiban.Dcb.WasmRuntime.Aspire package) wires the public
│                            runtime container + Postgres.
├── MyWeather.Domain.Tests/  xUnit domain tests (IncludeTests=true, the default).
├── scripts/build-wasm.sh    Builds modules/MyWeather.wasm + config/sekiban-manifest.json.
├── scripts/smoke.sh         End-to-end smoke against the running container.
└── README.md
```

There is deliberately no solution file: the `.Wasm` project targets `wasi-wasm`
with NativeAOT-LLVM and must be built by `scripts/build-wasm.sh` (which
provisions the WASI SDK via Docker on macOS/Windows), never by a plain
`dotnet build`.

## Run

```bash
cd MyWeather
bash scripts/build-wasm.sh                # WASM module + runtime manifest
dotnet run --project MyWeather.AppHost    # Postgres + public runtime container
# or the end-to-end smoke (starts the AppHost itself):
bash scripts/smoke.sh
```

The runtime image tag defaults to `1.0.0-preview.3`; override with
`SAMPLE_RUNTIME_IMAGE_TAG`. Without Docker you can still build and test the
Domain/AppHost/Tests projects (`dotnet build`, `dotnet test`) — only the wasm
build (on macOS/Windows) and the live container run need Docker.

Prerequisites: .NET 10 SDK, Docker.

## Package restore note (pre-publish)

Generated AppHosts reference the `Sekiban.Dcb.WasmRuntime.Aspire` NuGet
package. Until its first NuGet publish, pack it locally and add a local
package source next to the generated solution:

```bash
dotnet pack src/lib/Sekiban.Dcb.WasmRuntime.Aspire/Sekiban.Dcb.WasmRuntime.Aspire.csproj \
  -c Release -o <local-packages-dir>
```

`scripts/templates/test-sekiban-wasm-decider.sh` automates exactly this
(local source + nuget.org NuGet.config).

## Validation and release lane

- Generation test: `bash scripts/templates/test-sekiban-wasm-decider.sh` —
  packs the template (and the Aspire dependency), installs it from the local
  nupkg, generates with `IncludeTests` on and off under a custom `-n` name,
  builds Domain + AppHost, runs the generated tests, and verifies that no
  `SekibanDcbDecider` residue survives sourceName substitution.
- Release lane: `.github/workflows/release-templates-preview.yml` — a
  published GitHub Release tagged `templates-v<version>` validates and (behind
  the protected `templates-release` environment) publishes the package.
  Releases with other tag prefixes are skipped. Publishing remains human-gated
  and is not exercised until the first-publish prerequisites are met.
