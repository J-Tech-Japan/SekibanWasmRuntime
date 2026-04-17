# Step 1 — Result

PR: `feat/csharp-decider-materialized-view`
Outcome: ✅ End-to-end verified locally against the C# Decider sample AppHost.

## What was delivered

- `SekibanDcbDecider.Wasm/MaterializedView/` — WASM-internal MV abstraction
  + `ClassRoomEnrollmentMvV1` projector ported into the WASM module
  + `mv_metadata` / `mv_initialize` / `mv_apply_event` exports
  + `HostBackedMvQueryPort` → `env.mv_host_query_rows` import binding
- `Sekiban.Dcb.WasmRuntime.Host/MaterializedView/`
  + `WasmtimeMaterializedViewExecutor` — real Wasmtime Linker wiring, shares
    `WasmtimeRuntime` + `WasmtimeModuleCache` with the multi-projection pool
  + `WasmBackedMaterializedViewProjector` — `IMaterializedViewProjector`
    shim, caches `MvTableBindings` from `InitializeAsync`
  + `MvParamDapperBridge` — turns `MvParam[]` back into Dapper `DynamicParameters`
  + `WasmMvBoundaryContracts.cs` — host-side DTO mirrors
- `Sekiban.Dcb.WasmRuntime.Host/SekibanRuntimeManifest.cs`
  + `MaterializedViews[]` section and `SekibanRuntimeMaterializedView` type
- `Sekiban.Dcb.WasmRuntime.Host/Program.cs`
  + MV runtime DI (`AddSekibanDcbMaterializedView` +
    `AddSekibanDcbMaterializedViewPostgres(registerHostedWorker: true)` +
    `AddSekibanDcbMaterializedViewOrleans`)
  + `UnwrapDiscriminatedTagPayload` for `ClassRoomProjector`'s union payload
- `SekibanDcbDecider.ClientApi/Endpoints/MaterializedViewEndpoints.cs`
  + Read-only `/api/mv/classrooms`, `/api/mv/students`, `/api/mv/enrollments`,
    `/api/mv/status` via `IMvRegistryStore` + Dapper. No Orleans client needed.
- `SekibanDcbDecider.AppHost/Program.cs`
  + `SekibanCSharpMvDb` Postgres DB wired to wasmserver (grain + catch-up) and
    to clientapi (read-only).
- `src/samples/.../modules/sekiban-runtime-manifest.json`
  + `materializedViews: [ { viewName: "ClassRoomEnrollment", viewVersion: 1,
    logicalTables: ["classrooms","students","enrollments"] } ]`

Version bumps:

- Sekiban.Dcb.* NuGet refs: `10.1.12` → `10.1.18`
- Sekiban submodule pinned at `2f16cc32` (origin/main)
- Added Sekiban.Dcb.MaterializedView / .Postgres / .Orleans package versions
  to `Directory.Packages.props` + `Dapper 2.1.66`.

Infrastructure fix-ups needed to make the build work locally on macOS arm64:

- Copied the pre-built `libwasmtime.dylib` from a sibling worktree into
  `external/wasmtime-dotnet/src/obj/wasmtime-dev-aarch64-macos-c-api/lib/`.
- Rebuilt the sample `.wasm` via `build/scripts/build-sample-csharp-wasm.sh`
  (Docker mode auto-downloads WASI SDK 29 inside the container).

## Verification matrix

Run against the AppHost on 2026-04-17, C# Decider sample,
`SekibanCSharpMvDb` starting empty.

| Scenario                                            | Result |
|-----------------------------------------------------|--------|
| Create 3 classrooms (`Math 101`, `Physics 201`, `Chemistry 301`) | ✅ reflected in `/api/mv/classrooms` |
| Create 3 students (`Alice`, `Bob`, `Carol`)         | ✅ reflected in `/api/mv/students` |
| Enroll Carol → Chemistry 301                        | ✅ `enrollments` row + `enrolled_count` recount |
| Drop Carol                                          | ✅ `enrollments` removed + counts back to 0 |
| Enroll Alice + Bob → Chemistry (max=2)              | ✅ both enrolled, `enrolled_count=2` |
| Enroll Carol → Chemistry (already full)             | ✅ 500 rejected by domain (`FilledClassRoomState` branch) |
| Restart wasmserver → catch-up re-runs all events    | ✅ idempotent (same state, no duplicate rows) |
| Parallel: 5 pairs (class + student + enroll) concurrent | ✅ all 5 classrooms + 5 students + 5 enrollments land |
| Direct psql against `sekiban_mv_classroomenrollment_v1_*` | ✅ matches API |
| DbGate visual: both DBs visible from the postgres server resource | ✅ |

## Caveat: Enrollment 500 fix

`RemoteCommandContext.GetTagStateAsync` was throwing
`Payload type 'ClassRoomState' is not registered`. Cause: the host's
manifest-inferred name `ClassRoomState` doesn't match any CLR type
(`ClassRoomProjector` produces either `AvailableClassRoomState` or
`FilledClassRoomState`).

Fix on the host side (`Program.cs`): parse the snapshot JSON, read
`stateKind`, return the inner JSON + the matching CLR type name. This is
localized to `ClassRoomProjector`; future discriminated-union projectors will
need the same treatment (which is a strong argument for the Sekiban-side
abstraction proposal in Step 2).

## Files touched

See `git diff --stat` on branch `feat/csharp-decider-materialized-view`.
