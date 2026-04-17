# Materialized View × WASM Runtime — Design

## Goal

Run Sekiban.Dcb.MaterializedView inside a silo that hosts the **WASM** runtime,
so projector logic — which uses CLR pattern-matching on event payloads — runs
inside the WASM module, while the grain/executor/registry infrastructure stays
CLR-side. Command-side grains and MultiProjection grain already follow this
Native/WASM split; MaterializedView grain joins the same pattern.

## Architecture

```
Orleans silo (Sekiban.Dcb.WasmRuntime.Host)
│
├── MaterializedViewGrain  (Sekiban.Dcb.MaterializedView.Orleans — unchanged)
│     │  subscribes to EventStreamProvider + periodic catch-up
│     │
│     └── IMvExecutor = PostgresMvExecutor (unchanged)
│           │  reads events from IEventStore, drives projector, executes SQL in txn
│           │
│           └── IMaterializedViewProjector = WasmBackedMaterializedViewProjector (NEW)
│                 │
│                 └── IWasmMaterializedViewExecutor = WasmtimeMaterializedViewExecutor (NEW)
│                       │   single Wasmtime Store/Instance per host, serialized access
│                       │
│                       └── WASM module exports:
│                             mv_metadata()                     → list of MVs
│                             mv_initialize(view, bindings)     → DDL SQL batch
│                             mv_apply_event(view, b, event)    → SQL batch
│                           WASM imports (host):
│                             env.mv_host_query_rows(sql, params, rowLimit) → JSON result
│
└── MvCatchUpWorker (BackgroundService, registerHostedWorker=true)
      drives catch-up polling on top of what the grain stream does, so progress
      is guaranteed even when no Orleans stream publisher is wired up (current
      state in wasm runtime host)

ClientApi (read side, no Orleans client)
  uses IMvRegistryStore (Postgres) to resolve logical→physical table names,
  then queries MV Postgres directly via Dapper.
```

## WASM boundary DTOs

Defined in `SekibanDcbDecider.Wasm/MaterializedView/WasmMvContracts.cs`,
mirrored host-side in `Sekiban.Dcb.WasmRuntime.Host/MaterializedView/WasmMvBoundaryContracts.cs`:

- `MvParamKind` / `MvParam` — typed scalar params (String / Guid / Int32 /
  Int64 / Boolean / DateTimeOffset / Decimal / Double / Bytes / Null).
- `MvSqlStatementDto` — `{ sql, parameters: MvParam[] }`.
- `MvTableBindingsDto` — logical → physical table name map.
- `MvSerializableEventDto` — `{ eventType, payloadJson, sortableUniqueId, tags[] }`.
- `MvStatementBatchDto` — response envelope for initialize / apply.
- `MvQueryRowDto` / `MvQueryResultDto` — host-import query callback result.
- `WasmMvMetadata` — metadata emitted by `mv_metadata`.

All crossing the boundary as JSON. AOT-safe — `MvParamBuilder` serializes
scalars with hand-rolled JSON escape to avoid reflection-based
`JsonSerializer.Serialize<T>`.

## Key design choices

### Why WASM-internal `IWasmMvProjector` instead of reusing
`IMaterializedViewProjector`

`Sekiban.Dcb.MaterializedView` pulls in `MvRowMapper<T>` which uses
`System.Linq.Expressions` to build compiled getters. That is incompatible with
`wasi-wasm` + ILCompiler.LLVM. The WASM side therefore exposes a minimal
self-contained interface, and the host-side shim bridges it back into the
Sekiban `IMaterializedViewProjector` surface.

### Physical table name resolution

`IMvRegistryStore` (Postgres) is the single source of truth for physical names
(`serviceId × viewName × viewVersion × logicalTable` → physical). The shim:

1. Calls `ctx.RegisterTable(logical)` during `InitializeAsync` — registry
   computes the physical name.
2. Caches `MvTableBindingsDto { logical → physical }` per projector instance.
3. Passes the bindings into every `mv_initialize` / `mv_apply_event` WASM call.

`IMvApplyContext.GetDependencyViewTable` is **not** used for same-view tables
— Sekiban treats that call as cross-view and throws `NotSupportedException` in
the PoC.

### Discriminated-union tag payloads

`ClassRoomProjector` returns either `AvailableClassRoomState` or
`FilledClassRoomState`. The default `InferTagPayloadName` in
`SekibanRuntimeManifest` can only produce one name. The WASM side wraps the
payload in a `ClassRoomProjectorSnapshot { stateKind, availableState,
filledState }`. The host's tag-state endpoint (`Program.cs
UnwrapDiscriminatedTagPayload`) inspects `stateKind` at serve time, extracts
the inner JSON, and sets `TagPayloadName` to the real CLR type name so
`RemoteCommandContext` on ClientApi can deserialize it.

### Sync mid-apply DB query

`mv_host_query_rows` is a Wasmtime host callback wired via
`Function.FromCallback`. It reads SQL + params from WASM linear memory,
retrieves the current `IMvApplyContext` from an `AsyncLocal`, calls Dapper
synchronously via `.GetAwaiter().GetResult()`, allocates a result buffer in
WASM memory via the module's `alloc` export, and writes JSON back. This is
safe because `MaterializedViewGrain` serializes apply operations per view.
