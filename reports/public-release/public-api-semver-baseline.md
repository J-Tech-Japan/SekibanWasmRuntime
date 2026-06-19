# Public API And SemVer Baseline

Issue: `#161` / `SWR-G024 Public API and SemVer baseline snapshot`

## Scope

This report records the source-level public API baseline for the initial
SekibanWasmRuntime preview NuGet packages. It is a preview compatibility
baseline, not a permanent API freeze and not a package publication step.

- Baseline commit inspected: `5d50bd1`
- Package version line: `1.0.0-preview.*`
- Target framework: `net10.0`
- Public package projects:
  - `src/lib/Sekiban.Dcb.WasmRuntime/Sekiban.Dcb.WasmRuntime.csproj`
  - `src/lib/Sekiban.Dcb.WasmRuntime.Remote/Sekiban.Dcb.WasmRuntime.Remote.csproj`
  - `src/lib/Sekiban.Dcb.WasmRuntime.Wasmtime/Sekiban.Dcb.WasmRuntime.Wasmtime.csproj`

## Refresh Method

Refresh this report before a public preview release when any file under
`src/lib/Sekiban.Dcb.WasmRuntime*` changes public type, member, package
metadata, dependency shape, or serialized DTO behavior.

```bash
find src/lib/Sekiban.Dcb.WasmRuntime \
  src/lib/Sekiban.Dcb.WasmRuntime.Remote \
  src/lib/Sekiban.Dcb.WasmRuntime.Wasmtime \
  -path '*/obj/*' -prune -o -path '*/bin/*' -prune -o -name '*.cs' -print \
  | sort \
  | xargs /usr/bin/grep -HnE '^[[:space:]]*public (sealed |static |abstract |partial )?(class|record|interface|enum|struct)|^[[:space:]]*public (static )?[A-Za-z0-9_<>,?\.]+ [A-Za-z0-9_]+\('
```

If a future PR introduces a full API-diff tool, keep this report as the human
summary and link the generated diff artifact from the corresponding release
evidence.

## Package Baseline

| Package ID | Project | Public contract role |
| --- | --- | --- |
| `Sekiban.Dcb.WasmRuntime` | `src/lib/Sekiban.Dcb.WasmRuntime/Sekiban.Dcb.WasmRuntime.csproj` | Core runtime abstractions, serialized command/query DTOs, in-process client abstractions, WASM projection registry/runtime, and DI registration helpers. |
| `Sekiban.Dcb.WasmRuntime.Remote` | `src/lib/Sekiban.Dcb.WasmRuntime.Remote/Sekiban.Dcb.WasmRuntime.Remote.csproj` | HTTP serialized DCB client, remote query client, remote executor, remote projection host/instance, and remote runner options. |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `src/lib/Sekiban.Dcb.WasmRuntime.Wasmtime/Sekiban.Dcb.WasmRuntime.Wasmtime.csproj` | In-process Wasmtime projection host, Wasmtime options, module/runtime helpers, binary format detection, and DI registration helpers. |

## Core Package Public Surface

Package: `Sekiban.Dcb.WasmRuntime`

Primary namespaces:

- `Sekiban.Dcb.WasmRuntime`
- `Sekiban.Dcb.Runtime`

Public type baseline:

- Runtime and resolver abstractions:
  `IProjectionRuntime`, `IProjectionState`, `IProjectorRuntimeResolver`,
  `IProjectionActorHost` integration via `WasmProjectionActorHostFactory` and
  `WasmProjectionActorHost`.
- Serialized DCB client and executor abstractions:
  `ISerializedDcbClient`, `ISerializedQueryClient`,
  `ISerializedCommandExecutor`, `ISekibanWasmExecutor`,
  `ISekibanCommandCommitRequestBuilder`,
  `IPersistedSerializableEventObserver`.
- Event/projection instance contracts:
  `ISerializableEventBatchProjectionInstance`,
  `IFreshPrimitiveProjectionHost`, `IPooledPrimitiveProjectionLeaseControl`.
- Runtime implementations and state:
  `CompositeProjectionRuntime`, `ProjectorRuntimeResolver`,
  `InProcSerializedDcbClient`, `SekibanWasmExecutor`,
  `WasmProjectionRuntime`, `WasmProjectionState`,
  `WasmTagStateProjectionPrimitive`,
  `WasmTagStateProjectionPrimitiveFactory`.
- Registration and configuration:
  `SekibanDcbRuntimeServiceCollectionExtensions`,
  `WasmRuntimeServiceCollectionExtensions`, `WasmProjectorRegistry`,
  `WasmTagStateOptions`, `WasmRuntimeMode`.
- Serialized public DTOs and helpers:
  `SerializedCommandExecuteRequest`, `SerializedCommandOptions`,
  `SerializedCommandExecuteResponse`, `SerializedCommandEventCandidate`,
  `SerializedQueryRequest`, `SerializedQueryResponse`,
  `SerializedListQueryResponse`, `SerializedCommandTypeRegistry`,
  `PersistedSerializableEventFactory`.
- WASM module/state records:
  `WasmModuleRef`, `WasmStateSnapshot`,
  `WasmProjectionActorHost.WasmCheckpointState`.

## Remote Package Public Surface

Package: `Sekiban.Dcb.WasmRuntime.Remote`

Primary namespace:

- `Sekiban.Dcb.WasmRuntime.Remote`

Public type baseline:

- Client/executor APIs:
  `HttpSerializedDcbClient`, `HttpSerializedQueryClient`,
  `RemoteSekibanExecutor`.
- Projection host APIs:
  `CompositePrimitiveProjectionHost`, `RemotePrimitiveProjectionHost`,
  `RemotePrimitiveProjectionInstance`.
- Command context and result bridging:
  `RemoteCommandContext`, `SerializedCommitResultRepublisher`.
- Configuration:
  `RemoteRunnerOptions`, `SerializedDcbClientOptions`.

## Wasmtime Package Public Surface

Package: `Sekiban.Dcb.WasmRuntime.Wasmtime`

Primary namespace:

- `Sekiban.Dcb.WasmRuntime.Wasmtime`

Public type baseline:

- Projection host/runtime APIs:
  `WasmtimePrimitiveProjectionHost`, `WasmtimePrimitiveProjectionInstance`,
  `WasmtimeRuntime`, `WasmtimeModuleCache`.
- Configuration and DI:
  `WasmtimeHostOptions`, `WasmtimeServiceCollectionExtensions`.
- Preview platform helpers:
  `WasmBinaryFormatDetector`, `WasmtimePreview2ShimResolver`,
  `WasmtimeProjectionWarmupService`.

The Wasmtime package remains preview-only while native asset packaging and host
policy are finalized. Public constructor signatures, DI extension names,
option-property names, binary detection behavior, and the native dependency
packaging shape are still consumer-visible and require release notes when they
change.

## Breaking-Change Criteria

During preview, a change is treated as breaking when it can require a package
consumer, sample consumer, or downstream hosted integration to change code,
configuration, serialized payload handling, package selection, or deployment
behavior. That includes:

- removing, renaming, moving, or changing accessibility of any public type
  listed above;
- changing public constructor, method, property, record positional parameter,
  enum, or interface-member shape;
- changing serialized DTO property names, nullability expectations, result
  wrappers, or JSON behavior used by remote clients;
- changing DI extension method names, option names, required service
  registrations, or default runtime mode behavior;
- changing package IDs, package references exposed to consumers, target
  framework, or the Wasmtime native asset packaging behavior;
- changing compatibility assumptions documented in
  `reports/compatibility/serialized-dcb-contract-black-box-baseline.md`.

Breaking public contract changes require all of the following before release:

- `CHANGELOG.md` names the breaking change.
- `docs/release/migration-notes.md` includes affected packages, required
  consumer action, compatibility impact, and fallback if one exists.
- `reports/compatibility/serialized-dcb-contract-black-box-baseline.md` is
  refreshed when serialized public contracts change, or a release-blocking note
  explains why evidence is unavailable.
- GitHub Release notes link the migration entry.

## Current Baseline Classification

The initial public preview package surface is ready to use as the comparison
baseline for future preview releases. No breaking public contract change is
introduced by creating this report.

## Verification

- Source-level API inspection command above was run against the three public
  package directories and returned 178 public declaration/member matches.
- Package metadata was checked in each public `.csproj`.
- `dotnet build --no-restore` passed for all three public package projects. The
  core package build reported the existing nullable warning in
  `WasmProjectionRuntime.cs`; the remote and Wasmtime package builds were clean.
- `git diff --check` must pass before merging the release PR.
