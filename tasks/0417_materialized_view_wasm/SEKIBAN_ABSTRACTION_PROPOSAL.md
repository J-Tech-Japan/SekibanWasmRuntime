# Sekiban Upstream Proposal — `IMvApplyHost` Abstraction

Parallels the existing `IProjectionActorHost` pattern used by MultiProjection
to let Native and WASM plug in via DI, but applied to the MaterializedView
grain path.

This document is the starting brief for a Sekiban-side Issue / PR. It belongs
in this SekibanWasmRuntime repo (not in Sekiban) until the proposal is
submitted — then link here from the Sekiban issue body.

## Why

Today's Sekiban MV grain is:

```
MaterializedViewGrain (grain)
  ├─ IMvExecutor = PostgresMvExecutor (engine)
  │     └─ IMaterializedViewProjector (user code, CLR)
  └─ IMvRegistryStore / IMvStorageInfoProvider
```

`IMaterializedViewProjector` is user-facing, CLR-typed:

```csharp
public interface IMaterializedViewProjector
{
    string ViewName { get; }
    int ViewVersion { get; }
    Task InitializeAsync(IMvInitContext ctx, CancellationToken);
    Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(Event ev, IMvApplyContext ctx, CancellationToken);
}
```

Two things make it awkward to plug in a WASM backend via DI without a shim:

1. `ApplyToViewAsync(Event ev, ...)` takes a CLR `Event` with a strongly-typed
   `Payload`. In WASM-mode hosts, `Payload` is `DynamicJsonEventPayload` and
   needs unpacking. A shim that casts and re-serializes works (see
   SekibanWasmRuntime's `WasmBackedMaterializedViewProjector`) but is a
   leaky abstraction — it depends on how the host sets up `IEventTypes`.

2. `MvSqlStatement { string Sql, object? Parameters }` uses an untyped
   `object?` for parameters that Dapper reflects over. Crossing a language
   boundary forces an ad-hoc DTO (see SekibanWasmRuntime's
   `MvParam`/`MvParamKind`/`MvSqlStatementDto`). Different hosts invent
   different DTOs.

3. Discriminated-union tag payloads (e.g. `ClassRoomProjector` returning
   `AvailableClassRoomState | FilledClassRoomState`) cannot be represented
   with the manifest-inferred single tag-payload name. The WASM runtime has
   to patch this post-hoc per-projector.

## Proposal

Introduce an **engine-internal** abstraction below the user-facing projector:

```csharp
public readonly record struct MvParam(string Name, MvParamKind Kind, string? ValueJson);
public readonly record struct MvSqlStatementDto(string Sql, IReadOnlyList<MvParam> Parameters);

public interface IMvTableBindings
{
    string GetPhysicalName(string logicalName);
    IReadOnlyDictionary<string, string> LogicalToPhysical { get; }
}

public interface IMvApplyQueryPort  // replaces IMvApplyContext's Query* surface
{
    Task<IReadOnlyList<JsonElement>> QueryRowsAsync(string sql, IReadOnlyList<MvParam> params, CancellationToken);
    Task<JsonElement?> QuerySingleOrDefaultAsync(string sql, IReadOnlyList<MvParam> params, CancellationToken);
    Task<string?> ExecuteScalarJsonAsync(string sql, IReadOnlyList<MvParam> params, CancellationToken);
}

public interface IMvApplyHost
{
    string ViewName { get; }
    int ViewVersion { get; }
    IReadOnlyList<string> LogicalTables { get; }

    Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings, CancellationToken);

    Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
        SerializableEvent ev,             // pre-deserialization — host decides whether to expose typed Event
        IMvTableBindings tables,
        IMvApplyQueryPort queryPort,
        string sortableUniqueId,
        CancellationToken);
}

public interface IMvApplyHostFactory
{
    IMvApplyHost Create(string viewName, int viewVersion);
}
```

And a tag-payload envelope that carries the actual payload type name
(or a structured `kind` discriminator) so the host never has to infer from
the projector name.

## Native impl

`NativeMvApplyHost` wraps the existing user-written
`IMaterializedViewProjector`: deserializes `SerializableEvent` to `Event`
using `IEventTypes`, calls `projector.ApplyToViewAsync(ev, ctx)`, and
translates `MvSqlStatement.Parameters` (anonymous types) into the typed
`MvParam[]` DTO. Fully backward compatible — all existing projectors keep
working.

## WASM impl

`WasmMvApplyHost` calls the WASM module's `mv_apply_event(viewName,
viewVersion, tableBindings, serializableEventJson)` export, receives
`MvSqlStatementDto[]` back, and forwards. No JSON unwrapping on the host.
Mid-apply queries land on `IMvApplyQueryPort` which the grain satisfies via
Dapper.

## Changes requested in Sekiban core

- Introduce `IMvApplyHost` / `IMvApplyHostFactory` +
  `MvSqlStatementDto` / `MvParam` in `Sekiban.Dcb.MaterializedView`.
- Refactor `PostgresMvExecutor` to call `IMvApplyHost` instead of calling
  `IMaterializedViewProjector` directly.
- Provide `NativeMvApplyHost` that wraps the existing
  `IMaterializedViewProjector` (backward compatible).
- Optional: extend `SerializableTagState` to include an `ActualPayloadName`
  discriminator so multi-variant payloads (like `ClassRoomProjector`) don't
  need host-level JSON peeking.

## SekibanWasmRuntime changes after Sekiban merges

- Replace `WasmBackedMaterializedViewProjector` with `WasmMvApplyHost`.
- Remove `UnwrapDiscriminatedTagPayload` once the Sekiban-level envelope
  carries the actual type name.
- The per-sample MV wiring simplifies to a DI swap:
  `services.AddSingleton<IMvApplyHostFactory, WasmMvApplyHostFactory>()`.

## Draft Issue body

When the upstream issue is filed, the body should link back to this file and
to `STEP1_RESULT.md` for the motivating E2E evidence.
