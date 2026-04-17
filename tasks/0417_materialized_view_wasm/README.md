# Materialized View × WASM Runtime — Step 1 & Roadmap

Tracks the work that brings Sekiban's Materialized View (DCB, Postgres-backed)
runtime to SekibanWasmRuntime, plus follow-up design work for a cleaner
`IMvApplyHost` abstraction in Sekiban core.

## Status snapshot (2026-04-17)

- ✅ Step 1 (this PR): working MV grain inside `wasmserver`, WASM-side projector
  via `mv_*` exports, `mv_host_query_rows` host import, ClientApi read-only
  `/api/mv/*` endpoints, sample `ClassRoomEnrollmentMvV1` projector running.
- 🔜 Step 2: Sekiban-side abstraction PR (`IMvApplyHost` / parameter DTO) so
  Native/WASM can swap via DI cleanly.
- 🔜 Step 3: SekibanWasmRuntime migrates shim to `IMvApplyHost`, removes
  `WasmBackedMaterializedViewProjector : IMaterializedViewProjector` bridge.

Detailed design: [`DESIGN.md`](DESIGN.md)
Step 1 outcome: [`STEP1_RESULT.md`](STEP1_RESULT.md)
Follow-ups / known issues: [`FOLLOW_UPS.md`](FOLLOW_UPS.md)
Sekiban abstraction proposal (for Step 2): [`SEKIBAN_ABSTRACTION_PROPOSAL.md`](SEKIBAN_ABSTRACTION_PROPOSAL.md)
