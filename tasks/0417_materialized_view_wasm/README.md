# Materialized View × WASM Runtime — Step 1 & Roadmap

Tracks the work that brings Sekiban's Materialized View (DCB, Postgres-backed)
runtime to SekibanWasmRuntime, plus follow-up design work for a cleaner
`IMvApplyHost` abstraction in Sekiban core.

## Status snapshot (2026-04-17)

- ✅ Step 1 ([SekibanWasmRuntime#87](https://github.com/J-Tech-Japan/SekibanWasmRuntime/pull/87)): working MV grain inside `wasmserver`, WASM-side projector
  via `mv_*` exports, `mv_host_query_rows` host import, ClientApi read-only
  `/api/mv/*` endpoints, sample `ClassRoomEnrollmentMvV1` projector running.
- ✅ Step 2 Issue filed: [Sekiban#1029](https://github.com/J-Tech-Japan/Sekiban/issues/1029) —
  proposes `IMvApplyHost` abstraction + typed `MvParam`/`MvSqlStatementDto`
  DTO so Native/WASM can swap via DI cleanly. Timed to land alongside the
  Unsafe Window MV v1 redesign in Sekiban#1028 / #1027.
- 🔜 Step 3: once Sekiban#1029 merges, SekibanWasmRuntime migrates the shim
  to `IMvApplyHost`, removes
  `WasmBackedMaterializedViewProjector : IMaterializedViewProjector`,
  and retires `UnwrapDiscriminatedTagPayload`.

Detailed design: [`DESIGN.md`](DESIGN.md)
Step 1 outcome: [`STEP1_RESULT.md`](STEP1_RESULT.md)
Follow-ups / known issues: [`FOLLOW_UPS.md`](FOLLOW_UPS.md)
Sekiban abstraction proposal (for Step 2): [`SEKIBAN_ABSTRACTION_PROPOSAL.md`](SEKIBAN_ABSTRACTION_PROPOSAL.md)
