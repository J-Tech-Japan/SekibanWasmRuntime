# Implementation Tasks (0215 Simplified)

This file is written so next week implementation can start directly without extra design meetings.

## Phase 0: Baseline Lock (mandatory before coding)

### Task 0.1: Confirm reference revisions
- Record current revisions in issue comment:
  - `git rev-parse HEAD` (SekibanWasmRuntime)
  - `git -C submodules/Sekiban rev-parse HEAD`
- Acceptance:
  - Both SHAs are written in issue thread.

### Task 0.2: Confirm current abstraction on Sekiban side
- Verify these files in `submodules/Sekiban`:
  - `dcb/src/Sekiban.Dcb.Core/Runtime/ITagStateProjectionPrimitive.cs`
  - `dcb/src/Sekiban.Dcb.Orleans.Core/Grains/TagStateGrain.cs`
  - `dcb/src/Sekiban.Dcb.Orleans.Core/Runtimes/NativeTagStateProjectionPrimitive.cs`
- Acceptance:
  - Confirm that TagState uses accumulator contract:
    - `ApplyState(SerializableTagState?)`
    - `ApplyEvents(IReadOnlyList<SerializableEvent>, string?)`
    - `GetSerializedState()`

## Phase 1: Contract Alignment in SekibanWasmRuntime

### Task 1.1: Gap list between current WASM runtime and latest Sekiban contract
- Inspect and document mismatches in:
  - `src/lib/Sekiban.Dcb.WasmRuntime/*`
  - `src/lib/Sekiban.Dcb.WasmRuntime.Wasmtime/*`
  - `src/lib/Sekiban.Dcb.WasmRuntime.Remote/*`
- Required output:
  - Add section `Gap Matrix` to issue comment with columns:
    - `Contract`
    - `Current status`
    - `Fix path`

### Task 1.2: Define TagState runtime interface usage points
- Identify all call sites that should move to TagState primitive contract.
- File candidates:
  - `src/internalUsage/SekibanWasm.ApiService/Program.cs`
  - `src/internalUsage/DcbRuntime.WasmRunner/Program.cs`
  - any runtime registration extension files under `src/lib`
- Acceptance:
  - Every usage point is mapped to either:
    - `keep as-is`
    - `replace`
    - `remove`

## Phase 2: Runtime Wiring Refactor (no behavior regression)

### Task 2.1: Add explicit registration path for TagState primitive runtime
- Add DI registration entry points in `src/lib` so host app can choose runtime mode.
- Minimum requirement:
  - runtime registration must not depend on direct native construction assumptions.
  - registration must support replacement with WASM implementation.

### Task 2.2: Normalize runtime selection modes
- Ensure runtime mode selection (native/wasm/hybrid/remote) has deterministic behavior.
- Existing baseline file:
  - `src/internalUsage/SekibanWasm.ApiService/Program.cs`
- Fixes:
  - avoid hidden fallback paths.
  - fail fast on missing required configuration.

### Task 2.3: Add integration-focused comments for maintainability
- Add short comments only around non-obvious runtime switch logic.
- Keep comments minimal and factual.

Acceptance for Phase 2:
- Build succeeds.
- Runtime mode resolution is explicit and testable.

## Phase 3: TagState WASM Primitive Implementation

### Task 3.1: Implement WASM accumulator that mirrors native contract
- Add implementation in `src/lib` namespace dedicated to TagState primitive.
- Required behavior:
  - `ApplyState`: restore from serialized state once.
  - `ApplyEvents`: apply only incremental events ordered by sortable id.
  - `GetSerializedState`: serialize only on state change.

### Task 3.2: Version and identity guards
- Enforce rules identical to native expectation:
  - stale `ProjectorVersion` cache must not be reused.
  - mismatched `TagGroup`, `TagContent`, `TagProjector` must reset to empty state.

### Task 3.3: Error semantics parity
- Deserialize failures in state/event path must produce explicit failure (no silent empty fallback).

Acceptance for Phase 3:
- Same input stream produces same output fields as native:
  - `Version`
  - `LastSortedUniqueId`
  - `TagPayloadName`
  - `ProjectorVersion`

## Phase 4: Internal Usage Examples (must be runnable)

### Task 4.1: C# WASM internal usage path
- Verify and update:
  - `src/internalUsage/SekibanWasm.Wasm/*`
  - `src/internalUsage/modules/csharp-weather.wasm` generation path
- Ensure docs include exact command to regenerate module.

### Task 4.2: Rust WASM internal usage path
- Verify and update:
  - Rust module generation script and artifact path
  - `src/internalUsage/modules/rust-weather.wasm`
- Ensure docs include exact command to regenerate module.

### Task 4.3: Runner/API integration examples
- Ensure examples clearly show:
  - Local command path (client-side native)
  - Remote command path (WASM server-side)
  - TagState retrieval boundary

Acceptance for Phase 4:
- New contributor can run one C# WASM path and one Rust WASM path by following written steps only.

## Phase 5: Tests and Regression Control

### Task 5.1: Unit tests for WASM primitive behavior
- Add tests under `src/internalUsage/SekibanWasm.Tests` for:
  - initialization
  - incremental catch-up
  - persistence restore (same projector version)
  - persistence restore (different projector version)

### Task 5.2: Wiring tests for runtime selection
- Ensure tests cover native/wasm/hybrid/remote switches and required config failures.

### Task 5.3: CI command set
- Verify these commands in a CI-like environment:
```bash
./build/scripts/tests/test-build-csharp-wasm.sh
./build/scripts/tests/test-nuget-source-mapping.sh
./build/scripts/tests/test-nuget-wasm-config.sh
./build/scripts/tests/test-csproj-ilcompiler.sh

dotnet restore src/SekibanWasmRuntime.ci.slnx
dotnet build src/SekibanWasmRuntime.ci.slnx -c Release
dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build

./build/scripts/build-csharp-wasm.sh
./build/scripts/build-rust-wasm.sh
```

Acceptance for Phase 5:
- All required tests/commands pass.
- Issue checklist is fully completed.

## Out of Scope
- Changing Sekiban public API contracts from SekibanWasmRuntime side.
- Introducing Orleans-specific custom grain logic in SekibanWasmRuntime.
- Large refactors unrelated to TagState primitive/runtime alignment.
