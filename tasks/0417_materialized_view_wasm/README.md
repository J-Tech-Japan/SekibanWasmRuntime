# Materialized View × WASM Runtime — Step 1 & Roadmap

Tracks the work that brings Sekiban's Materialized View (DCB, Postgres-backed)
runtime to SekibanWasmRuntime, plus the follow-on migration to the upstream
`IMvApplyHost` abstraction that landed in Sekiban 10.2.0.

## Status snapshot (2026-04-17)

- ✅ Step 1 ([SekibanWasmRuntime#87](https://github.com/J-Tech-Japan/SekibanWasmRuntime/pull/87)): working MV grain inside `wasmserver`, WASM-side projector
  via `mv_*` exports, `mv_host_query_rows` host import, ClientApi read-only
  `/api/mv/*` endpoints, sample `ClassRoomEnrollmentMvV1` projector running.
- ✅ Step 2 Issue filed & resolved: [Sekiban#1029](https://github.com/J-Tech-Japan/Sekiban/issues/1029) —
  landed as [Sekiban PR#1030](https://github.com/J-Tech-Japan/Sekiban/pull/1030)
  (`IMvApplyHost`/`IMvApplyHostFactory`/`IMvApplyQueryPort` + typed
  `MvParam`/`MvSqlStatementDto`) and paired with
  [Sekiban PR#1031](https://github.com/J-Tech-Japan/Sekiban/pull/1031)
  (Unsafe Window MV v1). Both ship in the
  [`dcb-v10.2.0`](https://github.com/J-Tech-Japan/Sekiban/releases/tag/dcb-v10.2.0)
  release.
- ✅ Step 3 (this update): bumped to Sekiban 10.2.0, retired the shim
  projector (`WasmBackedMaterializedViewProjector`), replaced it with
  `WasmMvApplyHost` / `WasmMvApplyHostFactory` registered via
  `services.Replace<IMvApplyHostFactory>`. `UnwrapDiscriminatedTagPayload`
  now sets `SerializableTagState.ActualPayloadName` and the Remote
  executor/command-context use `ResolvedPayloadName` for deserialization.
  E2E (AppHost + enroll + MV query) verified green.

Detailed design: [`DESIGN.md`](DESIGN.md)
Step 1 outcome: [`STEP1_RESULT.md`](STEP1_RESULT.md)
Follow-ups / known issues: [`FOLLOW_UPS.md`](FOLLOW_UPS.md)
Sekiban abstraction proposal (for Step 2): [`SEKIBAN_ABSTRACTION_PROPOSAL.md`](SEKIBAN_ABSTRACTION_PROPOSAL.md)
