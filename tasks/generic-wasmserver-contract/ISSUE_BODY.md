## Background
WasmServer currently mixes generic runtime APIs and weather-specific APIs.
To support multiple ClientApi implementations (C# and Rust) with stable behavior, WasmServer must be fully generic.

Source of truth:
- `tasks/generic-wasmserver-contract/DESIGN.md`
- `tasks/generic-wasmserver-contract/IMPLEMENTATION_TASKS.md`

## Goal
Make WasmServer a generic runtime surface only, and move domain-facing API handling to ClientApi.

Target model:
- `[Web/UI] -> [ClientApi (C# or Rust)] -> [WasmServer (generic)]`

## Scope
### In scope
- Generic command execution contract (`/api/sekiban/serialized/command/execute`)
- Keep generic commit/tag-state/instances APIs
- Remove weather/domain routes from WasmServer
- C# and Rust ClientApi migration to generic contract
- Contract tests + integration tests + docs

### Out of scope
- Breaking Sekiban public API changes
- Orleans grain redesign

## Tasks
### Phase 1: Contract freeze
- [ ] Define DTO and JSON schema for `serialized/command/execute`
- [ ] Freeze error response shape

### Phase 2: WasmServer genericization
- [ ] Remove weather routes from C# WasmServer
- [ ] Remove weather routes from Rust WasmServer
- [ ] Add generic command execute endpoint

### Phase 3: ClientApi migration
- [ ] C# ClientApi local/remote through generic contract
- [ ] Rust ClientApi local/remote through generic contract

### Phase 4: tests/docs
- [ ] Contract/golden tests for C# and Rust parity
- [ ] E2E create/update/delete/query/tag-state
- [ ] README update with final topology

## Acceptance Criteria
- [ ] WasmServer has no domain-specific routes
- [ ] C# and Rust ClientApi both operate via same generic contract
- [ ] local and remote command paths are both supported
- [ ] CI passes for required tests
- [ ] docs are sufficient for implementation without chat context
