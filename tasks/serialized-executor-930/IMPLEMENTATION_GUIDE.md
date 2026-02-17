# IMPLEMENTATION GUIDE: Apply Sekiban PR #930 to SekibanWasmRuntime

## 0. Purpose
This guide defines exactly how to apply the merged Sekiban PR #930 contract to SekibanWasmRuntime.

Target: make SekibanWasmRuntime executable with the new serialized contract without needing to read external design docs.

Reference PR:
- https://github.com/J-Tech-Japan/Sekiban/pull/930

---

## 1. Mandatory Preflight (Hard Gate)
Do not start implementation until `submodules/Sekiban` is synchronized to latest `origin/main`.

Run:
```bash
git -C submodules/Sekiban fetch origin
git -C submodules/Sekiban checkout main
git -C submodules/Sekiban pull --ff-only origin main
git -C submodules/Sekiban rev-parse HEAD
```

Record in issue comment:
- SekibanWasmRuntime SHA: `git rev-parse HEAD`
- submodules/Sekiban SHA: `git -C submodules/Sekiban rev-parse HEAD`

Why required:
- PR #930 adds serialized executor contracts. If submodule is behind, runtime code cannot compile against the expected interfaces.

---

## 2. Contract Added by Sekiban PR #930
Implementation must use the following contract (from Sekiban.Dcb.Core):

- `ISerializedSekibanDcbExecutor`
  - `Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId)`
  - `Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(SerializedCommitRequest request, CancellationToken cancellationToken = default)`

- `SerializedCommitRequest`
  - `IReadOnlyList<SerializableEventCandidate> EventCandidates`
  - `IReadOnlyList<ConsistencyTagEntry> ConsistencyTags`

- `SerializableEventCandidate`
  - `byte[] Payload`
  - `string EventPayloadName`
  - `IReadOnlyList<string> Tags`

- `ConsistencyTagEntry`
  - `string Tag`
  - `string LastSortableUniqueId`

Notes from #930 behavior:
- Duplicate consistency tags must fail.
- Consistency tags not present in candidate tags must fail.
- Reservation flow is cancellation-safe and must not leak reservations.

---

## 3. Scope in SekibanWasmRuntime
### In Scope
1. Add serialized contract aware client path for Local/Remote command execution.
2. Add HTTP API shape in Wasm server side that can map to `ISerializedSekibanDcbExecutor`.
3. Ensure C# and Rust internal usages both use this common serialized boundary.
4. Keep projection runtime path compatible with existing TagState primitive runtime work.

### Out of Scope
- Changing Sekiban public API from SekibanWasmRuntime.
- Orleans grain redesign in SekibanWasmRuntime.

---

## 4. Implementation Tasks
## Phase A: Add shared contract integration in `src/lib`
A1. Add a small client-facing abstraction in lib for serialized operations.
- Example name: `ISerializedDcbClient` (or equivalent)
- Required methods:
  - `GetSerializableTagStateAsync`
  - `CommitSerializableEventsAsync`
- Return types must stay aligned with Sekiban contract DTOs.

A2. Add HTTP transport implementation in lib.
- Map to server endpoints:
  - `POST /api/sekiban/serialized/tag-state`
  - `POST /api/sekiban/serialized/commit`
- Payload uses JSON with byte[] represented in standard JSON base64.

A3. Add optional in-proc adapter in lib.
- Adapter wraps `ISerializedSekibanDcbExecutor` directly for in-proc/local tests.

Acceptance A:
- library can call serialized endpoints without referencing internalUsage projects.

## Phase B: Wasm server API integration (internalUsage)
B1. In API service project, expose serialized endpoints and bind to executor.
- Resolve `ISerializedSekibanDcbExecutor` from DI.
- No typed command deserialization in the API layer.

B2. Validate request at API boundary.
- Empty request handling follows executor behavior.
- Surface validation failures as clear client errors.

Acceptance B:
- API service can commit serialized events and fetch serializable tag state.

## Phase C: Client API integration (C# + Rust)
C1. C# client path
- Build `SerializedCommitRequest` directly from WASM command output.
- For local mode: keep native command execution route.
- For remote mode: call serialized endpoint.

C2. Rust client path
- Use same JSON contract as C# client path.
- Ensure consistency tags are explicitly passed, not inferred server-side.

Acceptance C:
- both C# and Rust can run remote commit through identical serialized wire contract.

## Phase D: Internal usages and docs
D1. Update internal usage runbooks with exact commands.
D2. Document mode switch matrix: local(native) vs remote(serialized).
D3. Add troubleshooting section for consistency tag mismatch and stale sortable id.

Acceptance D:
- a new contributor can run one C# remote scenario and one Rust remote scenario from docs only.

---

## 5. Required Tests
1. Contract tests
- unknown consistency tag -> fail
- duplicate consistency tags -> fail

2. Integration tests (API + client)
- serialized commit success path
- stale sortable id conflict path
- get serializable tag state success path

3. Mode-switch tests
- local/native path remains working
- remote/serialized path works with same domain behavior

---

## 6. Success Criteria
All are required:
1. `submodules/Sekiban` updated to latest `main` and SHA logged in issue.
2. C# and Rust remote flows both use serialized contract (`SerializedCommitRequest`).
3. Wasm server exposes serialized endpoints backed by `ISerializedSekibanDcbExecutor`.
4. Required tests pass in CI-compatible environment.
5. No dependency on external docs beyond this guide + issue.

---

## 7. Suggested PR Split
- PR1: lib contract + HTTP client transport
- PR2: API service endpoint wiring
- PR3: C#/Rust internal usage integration + docs + tests

---

## 8. Risk Notes
- If submodule SHA is not synchronized first, implementation may silently target old contracts.
- Inconsistent consistency tag creation between C# and Rust will create false concurrency failures.
- Keep one canonical JSON schema for both client implementations.
