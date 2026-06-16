# Public Release Readiness Inventory

Date: 2026-06-16
Repository: `J-Tech-Japan/SekibanWasmRuntime`
Base commit inspected: `31c49f5e8d9b8f056f90616e99aacd139b570ad1`
Issue: `#115` / `SWR-G001 Public release inventory and package inspection`

This report is an inventory only. It does not clean generated files, change
package metadata, change dependencies, add release workflows, or publish
packages.

## Executive Summary

The repository is buildable enough to pack the three public package candidates,
but it is not ready for a public NuGet release without follow-up work.

### Blockers

- The three public package candidates are versioned as stable `1.0.0`, while
  accepted direction for release is `1.0.0-preview.*`.
- All three package candidates emit NuGet package readme warnings.
- `Sekiban.Dcb.WasmRuntime.Wasmtime` emits `NU5104` because stable `1.0.0`
  depends on prerelease `Wasmtime [35.0.0-dev, )`.
- NuGet metadata is too sparse for public release quality: authors default to
  package IDs, repository URL is absent from the generated nuspec, tags are
  absent, and package README files are absent.

### Warnings

- Ten tracked `.wasm` files are present, including several very large generated
  sample artifacts.
- Generated or vendored web assets are tracked in sample `wwwroot` trees,
  including 220 files under Bootstrap `dist` directories.
- `src/lib/sekiban-ts/dist/` contains tracked generated JavaScript and
  declaration output.
- `benchmarks/results/` contains 15 tracked `.log` files, while many more local
  ignored benchmark logs exist.
- The basic secret-pattern scan found sample fixed passwords and
  connection-string parsing code. No obvious cloud token/private-key pattern was
  found in the sampled output, but sample credentials should be reviewed before
  public release.

### Acceptable With Rationale

- `dotnet pack` succeeds for all three package candidates.
- `LICENSE`, `NOTICE`, and README license sections exist and identify Elastic
  License 2.0 plus third-party attribution intent.
- Latest observed `main` CI run succeeded.
- The Wasmtime package includes native `libwasmtime.dylib` content in the
  generated package; this is expected for a host integration package, but needs
  explicit cross-platform packaging policy before release.

## Commands And Evidence

### Repository And Toolchain

Command:

```bash
git status --short --branch
```

Result:

```text
## codex/swr-g001-public-release-inventory...origin/main
```

Command:

```bash
dotnet --info
```

Result summary:

- SDK: `10.0.100`
- Workload: `wasi-experimental` installed
- Host runtime: `11.0.0-preview.3.26207.106`
- `global.json`: `/Users/tomohisa/dev/GitHub/SekibanWasmRuntime/global.json`
- `global.json` pins SDK `10.0.100` with `rollForward: latestFeature`

### Latest Main CI

Command:

```bash
gh run list --repo J-Tech-Japan/SekibanWasmRuntime --branch main --limit 5 --json databaseId,status,conclusion,workflowName,headBranch,headSha,createdAt,updatedAt,url
```

Latest `main` run observed:

| Workflow | Run | Status | Conclusion | Head SHA | Created |
| --- | ---: | --- | --- | --- | --- |
| `ci` | `25705013467` | `completed` | `success` | `31c49f5e8d9b8f056f90616e99aacd139b570ad1` | `2026-05-12T00:12:28Z` |

CI is green on `main`, but package and repository release readiness still need
the follow-up work below.

## Package Inspection

Pack output directory used for inspection: `/tmp/sekiban-wasm-pack-115`

Command:

```bash
dotnet pack src/lib/Sekiban.Dcb.WasmRuntime/Sekiban.Dcb.WasmRuntime.csproj -c Release -o /tmp/sekiban-wasm-pack-115 --nologo
dotnet pack src/lib/Sekiban.Dcb.WasmRuntime.Remote/Sekiban.Dcb.WasmRuntime.Remote.csproj -c Release -o /tmp/sekiban-wasm-pack-115 --nologo
dotnet pack src/lib/Sekiban.Dcb.WasmRuntime.Wasmtime/Sekiban.Dcb.WasmRuntime.Wasmtime.csproj -c Release -o /tmp/sekiban-wasm-pack-115 --nologo
```

### `Sekiban.Dcb.WasmRuntime`

Package file: `/tmp/sekiban-wasm-pack-115/Sekiban.Dcb.WasmRuntime.1.0.0.nupkg`

Pack result:

- Succeeded.
- Warning: `CS8604` in `WasmProjectionRuntime.cs`.
- NuGet warning text: package is missing a readme.

Nuspec summary:

- `id`: `Sekiban.Dcb.WasmRuntime`
- `version`: `1.0.0`
- `authors`: `Sekiban.Dcb.WasmRuntime`
- `license`: file `LICENSE`
- `description`: `Sekiban DCB WASM runtime core abstractions and common types`
- `repository`: type `git`, commit only; no repository URL
- Dependencies: `Sekiban.Dcb.Core`, `Sekiban.Dcb.Core.Model`,
  `Sekiban.Dcb.Orleans.Core`, all `10.2.2`
- Package files: `LICENSE`, nuspec, `lib/net10.0/Sekiban.Dcb.WasmRuntime.dll`

Classification: blocker for metadata/readme/version policy, warning for
nullable warning.

### `Sekiban.Dcb.WasmRuntime.Remote`

Package file:
`/tmp/sekiban-wasm-pack-115/Sekiban.Dcb.WasmRuntime.Remote.1.0.0.nupkg`

Pack result:

- Succeeded.
- NuGet warning text: package is missing a readme.

Nuspec summary:

- `id`: `Sekiban.Dcb.WasmRuntime.Remote`
- `version`: `1.0.0`
- `authors`: `Sekiban.Dcb.WasmRuntime.Remote`
- `license`: file `LICENSE`
- `description`: `Sekiban DCB WASM runtime remote HTTP runner client`
- `repository`: type `git`, commit only; no repository URL
- Dependencies: `Sekiban.Dcb.WasmRuntime` `1.0.0`,
  `Sekiban.Dcb.WithoutResult` `10.2.2`,
  `Sekiban.Dcb.WithoutResult.Model` `10.2.2`
- Package files: `LICENSE`, nuspec,
  `lib/net10.0/Sekiban.Dcb.WasmRuntime.Remote.dll`

Classification: blocker for metadata/readme/version policy.

### `Sekiban.Dcb.WasmRuntime.Wasmtime`

Package file:
`/tmp/sekiban-wasm-pack-115/Sekiban.Dcb.WasmRuntime.Wasmtime.1.0.0.nupkg`

Pack result:

- Succeeded.
- Warning `NU5104`: stable package should not have prerelease dependency
  `Wasmtime [35.0.0-dev, )`.
- NuGet warning text: package is missing a readme.

Nuspec summary:

- `id`: `Sekiban.Dcb.WasmRuntime.Wasmtime`
- `version`: `1.0.0`
- `authors`: `Sekiban.Dcb.WasmRuntime.Wasmtime`
- `license`: file `LICENSE`
- `description`: `Sekiban DCB WASM runtime Wasmtime in-process host`
- `repository`: type `git`, commit only; no repository URL
- Dependencies: `Wasmtime` `35.0.0-dev`, `Sekiban.Dcb.WasmRuntime` `1.0.0`,
  `Microsoft.Extensions.DependencyInjection.Abstractions` `10.0.3`,
  `Microsoft.Extensions.Logging.Abstractions` `10.0.3`,
  `Microsoft.Extensions.Options` `10.0.3`
- Package files include:
  - `lib/net10.0/Sekiban.Dcb.WasmRuntime.Wasmtime.dll`
  - `content/libwasmtime.dylib`
  - `contentFiles/any/net10.0/libwasmtime.dylib`

Classification: blocker for stable/prerelease mismatch, metadata/readme/version
policy; warning for platform-specific native asset policy.

## Package Metadata Source Inspection

Command:

```bash
sed -n '1,220p' src/lib/Sekiban.Dcb.WasmRuntime/Sekiban.Dcb.WasmRuntime.csproj
sed -n '1,220p' src/lib/Sekiban.Dcb.WasmRuntime.Remote/Sekiban.Dcb.WasmRuntime.Remote.csproj
sed -n '1,220p' src/lib/Sekiban.Dcb.WasmRuntime.Wasmtime/Sekiban.Dcb.WasmRuntime.Wasmtime.csproj
sed -n '1,220p' Directory.Packages.props
sed -n '1,220p' Directory.Build.props
```

Observed:

- Each package project sets `IsPackable`, `PackageId`, and `Description`.
- None of the three package projects set package README, public repository URL,
  tags, company, product, package icon, release notes, or preview version.
- Central package management currently lists `Wasmtime` as `14.0.0`, but the
  generated Wasmtime package nuspec contains `Wasmtime` `35.0.0-dev` because
  the project compiles against `external/wasmtime-dotnet` and packages the
  prerelease dependency from that path.

## Tracked Artifact Inventory

Command:

```bash
git ls-files '*.wasm'
```

Tracked `.wasm` count: `10`

Largest tracked files from `git ls-files -z | xargs -0 ls -l | sort -k5 -nr`:

| Size | Path |
| ---: | --- |
| 61,459,787 | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Swift/modules/sekiban-dcb-decider-swift.wasm` |
| 37,430,316 | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/modules/sekiban-dcb-decider.wasm` |
| 35,946,329 | `src/internalUsages/cs/modules/csharp-weather.wasm` |
| 30,095,794 | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/modules/sekiban-dcb-decider.wasm` |
| 30,095,794 | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/modules/sekiban-dcb-decider.wasm` |
| 1,538,489 | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Go/modules/go-weather.wasm` |
| 881,491 | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/modules/sekiban-dcb-decider-rust.wasm` |
| 745,105 | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/modules/sekiban-dcb-decider-rust.wasm` |
| 286,125 | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/modules/ts-weather.wasm` |
| 285,734 | `src/internalUsages/rust/modules/rust-weather.wasm` |

Classification: warning. The accepted direction says generated sample `.wasm`
should be ignored and built from source through a common entry point, but this
slice only records the inventory.

## Generated, Vendored, And Host-Local Inventory

Commands:

```bash
git ls-files '.takt/*'
git ls-files 'src/lib/sekiban-ts/dist/*'
git ls-files '*/wwwroot/lib/bootstrap/dist/*'
git ls-files '*/wwwroot/*'
git ls-files 'benchmarks/results/*.log'
git status --short --ignored
```

Observed tracked files:

- `.takt/*`: `0` tracked files. `.takt/` is ignored and exists locally.
- `src/lib/sekiban-ts/dist/*`: `6` tracked generated JS/declaration files.
- `*/wwwroot/lib/bootstrap/dist/*`: `220` tracked Bootstrap distribution files.
- `*/wwwroot/*`: `233` tracked sample web assets.
- `benchmarks/results/*.log`: `15` tracked benchmark log files.

Observed ignored local residue includes `.claude/settings.local.json`,
`.takt/`, `.vscode/settings.json`, `artifacts/`, many `bin/` and `obj/`
directories, local benchmark logs, Playwright `node_modules`, sample
`node_modules`, `.next`, `.generated`, Rust `target`, Swift `.build`, MoonBit
`_build`, and MoonBit `.mooncakes`.

Classification: warning. These are repository hygiene follow-ups, not blockers
to producing this inventory.

## License, Notice, And README Evidence

Commands:

```bash
sed -n '1,80p' LICENSE
sed -n '1,80p' NOTICE
sed -n '1,80p' README.md
```

Observed:

- `LICENSE` exists and is Elastic License 2.0.
- `NOTICE` exists and lists SekibanWasmRuntime copyright plus third-party
  attributions for Sekiban, .NET Aspire, Orleans, Wasmtime/wasmtime-dotnet,
  Hummingbird, Swift libraries, Postgres NIO, Hono/pg/tsx/Next.js/React/tRPC,
  Tailwind CSS, Dapper, and Npgsql.
- README has a License section explaining the ELv2 boundary and links to
  `LICENSE`, `NOTICE`, and `CONTRIBUTING.md`.

Classification: acceptable with rationale for repository-level license
evidence. Package-level metadata and package README are still blockers.

## Basic Secret-Pattern Scan

Commands:

```bash
git ls-files | rg -i '(^|/)(\.env|\.env\..*|.*secret.*|.*credential.*|.*token.*|id_rsa|id_ed25519|\.pem|\.pfx|\.key)$|appsettings.*\.json$|local\.settings\.json$'
rg -n --hidden --glob '!submodules/**' --glob '!external/wasmtime-dotnet/**' --glob '!**/bin/**' --glob '!**/obj/**' --glob '!**/node_modules/**' --glob '!**/package-lock.json' --glob '!**/*.wasm' --glob '!**/*.dll' --glob '!**/*.png' --glob '!**/*.map' '(AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|AIza[0-9A-Za-z\-_]{35}|-----BEGIN (RSA |OPENSSH |EC |DSA )?PRIVATE KEY-----|password\s*=|client_secret\s*=|api[_-]?key\s*=)' .
```

Observed:

- Many tracked `appsettings*.json` files exist across samples.
- `generate_azure_credentials.sh` appears in multiple sample infrastructure
  paths.
- Pattern hits are sample fixed passwords such as `Sekiban1234%`, benchmark
  auth payload variables, and connection-string parsing code.
- No obvious AWS key, GitHub token, Google API key, or private-key block was
  found in the sampled output.

Classification: warning. The sample fixed passwords may be acceptable for local
demo flows, but public release should explicitly mark them as sample-only or
move them behind documented local configuration.

## Follow-Up Mapping

| Follow-up | Classification | Evidence |
| --- | --- | --- |
| `SWR-G002` NuGet metadata/readme/version policy | blocker | All three packages use stable `1.0.0`, lack package readme, sparse authors, no repository URL, no tags. |
| `SWR-G003` Wasmtime stability decision | blocker | `Sekiban.Dcb.WasmRuntime.Wasmtime` warns `NU5104` for `Wasmtime [35.0.0-dev, )`; package includes native `libwasmtime.dylib`. |
| `SWR-G004` Repo hygiene cleanup | warning | Tracked `dist`, `wwwroot` vendored assets, benchmark logs, and local ignored residue need policy. |
| `SWR-G005` WASM binary policy | warning | Ten tracked `.wasm` files include generated samples up to 61 MB. |
| `SWR-G006` Release workflow gate | blocker | CI is green, but no release gate evidence for package metadata/readme/nuspec inspection is recorded. |
| `SWR-G007` Public quickstart | warning | README has quickstart sections, but public package README/consumer quickstart is missing from `.nupkg`. |
| `SWR-G008` SaaS compatibility contract | warning | The package split suggests a public runtime/remote/Wasmtime boundary, but no durable compatibility contract evidence was found in package metadata. |

## Verification Performed

- `dotnet --info`: passed.
- `dotnet pack` for all three package candidates: passed with warnings above.
- `.nupkg`/nuspec inspection: completed from `/tmp/sekiban-wasm-pack-115`.
- Tracked large file and generated artifact inventory: completed with
  `git ls-files`.
- Basic secret-pattern scan: completed with allowlist notes above.
- Latest `main` CI status: observed successful `ci` run.

Final verification for this report should include `git diff --check` after the
file is committed.
