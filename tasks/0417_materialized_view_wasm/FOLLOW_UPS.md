# Follow-ups & Known Issues

Open items after Step 1 merge. Each item records the symptom, the cause, and
the remediation option so whoever picks this up (human or agent) has enough
context to act without reading the whole conversation.

## 1. `MaterializedViewGrainActivator` 30s timeout on cold start

**Symptom**: Aspire logs show
`Sekiban.Dcb.MaterializedView.Orleans.MaterializedViewGrainActivator[0] Failed
to activate materialized view grain for ClassRoomEnrollment/1.
System.TimeoutException: Response did not arrive on time in 00:00:30 ...`

**Cause**: first-time WASM instantiation + `_initialize` + JSON source-gen
context warmup inside `WasmtimeMaterializedViewExecutor.EnsureInstance` easily
exceeds Orleans' default 30s response timeout on cold boot (35 MB .wasm).

**Impact**: grain activation call from the BackgroundService fails, but the
grain itself continues running and completes init. Tables register, catch-up
runs, everything works — the log line is misleading.

**Remediation options**:
1. Precache the Wasmtime instance at host startup (before the activator runs)
   in a `BackgroundService` that eagerly calls `EnsureInstance`. Low-risk.
2. Raise Orleans `ResponseTimeout` in `Sekiban.Dcb.WasmRuntime.Host/Program.cs`
   for the MV grain path only (message option per-call).
3. Propose upstream: make activator timeout configurable via `MvOptions`.

Prefer option 1 — no Sekiban change, bounded to this host.

## 2. Upstream abstraction: `IMvApplyHost` (Step 2)

✅ Filed as [Sekiban#1029](https://github.com/J-Tech-Japan/Sekiban/issues/1029)
(2026-04-17). Timed to land alongside the Unsafe Window MV v1 redesign in
[Sekiban#1028](https://github.com/J-Tech-Japan/Sekiban/issues/1028) /
[Sekiban PR#1027](https://github.com/J-Tech-Japan/Sekiban/pull/1027) so the
new v1 contract carries typed `MvParam`/`MvSqlStatementDto` DTOs and an
`IMvApplyHost` seam from day one, rather than being retrofitted later.

Details: [`SEKIBAN_ABSTRACTION_PROPOSAL.md`](SEKIBAN_ABSTRACTION_PROPOSAL.md).
Step 1 uses a shim projector, which works but requires awkward payload
unwrapping and forces the WASM side to maintain a parallel DTO set. Once
Sekiban#1029 merges, Step 3 can retire the shim here.

## 3. `mv_host_query_rows` is implemented but unexercised

The `ClassRoomEnrollmentMvV1` projector only uses SQL subqueries for its
re-count logic, so the host-import callback path has never been triggered by
real traffic. Add a smoke test once a projector actually calls
`IWasmMvQueryPort.QueryRows` / `QuerySingleOrDefaultRow`.

Suggested smoke projector: a "top-N students per classroom" view that reads
the student table mid-apply.

## 4. Discriminated-union payload handling is per-projector hardcoded

`Program.cs UnwrapDiscriminatedTagPayload` is hardcoded for
`ClassRoomProjector` + `ClassRoomProjectorSnapshot`. If another projector
introduces a union payload, this helper needs to be extended (or generalized).

Preferred generalization: have the WASM module return the actual CLR type
name alongside the state so the host does not need per-projector knowledge.
This requires a Sekiban-level tag-state envelope change and is best folded
into Step 2.

## 5. Enrollment handler still requires the ClientApi domain types

`EnrollStudentInClassRoomHandler` (in `SekibanDcbDecider.EventSource`) uses
`context.GetStateAsync<ClassRoomProjector>(tag)` and pattern-matches the
returned `Payload`. That means ClientApi must reference the ImmutableModels
assembly to deserialize. OK today. If we later push the handler entirely into
the WASM module, this dependency goes away. Not scheduled.

## 6. `IEventPublisher` in the host silo

The WASM runtime host does not register an Orleans `IEventPublisher`, so the
MV grain's stream subscription never receives events; catch-up runs entirely
through the hosted `MvCatchUpWorker` polling the event store. This is fine for
correctness (the grain's registry position advances) but means real-time
latency is `MvOptions.PollInterval` (default 1s).

If we want sub-second latency, wire `OrleansEventPublisher` into
`ISerializedSekibanDcbExecutor`'s commit path so committed events land on the
`EventStreamProvider` stream that `MaterializedViewGrain` already subscribes
to.

## 7. Deployment / container image

The MV code is fully in the sample + host, no changes to CI / Dockerfile yet.
Before shipping to staging:

- Verify `build-csharp-wasm.sh` (CI script, not sample-specific) picks up MV
  exports — current `build-sample-csharp-wasm.sh` does; confirm the CI
  variant matches.
- Ensure the MV Postgres DB is provisioned in staging Aspire AppHost.

## 8. Tests

No new automated tests added in Step 1 — validation was manual. Before merge,
add at least:

- Unit test that `MvParamBuilder` produces parseable JSON round-trippable
  through `MvParamDapperBridge`.
- Integration test that spins up Aspire + asserts MV state after a known
  command sequence. Can reuse `build/scripts/e2e-clientapi-flow.sh` as a
  template.
