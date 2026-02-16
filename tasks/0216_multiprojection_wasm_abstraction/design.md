# SekibanWasmRuntime Follow-up Design for MultiProjection WASM Split

## Purpose
Track runtime-side implementation work after Sekiban core abstraction design is approved.

Sekiban prerequisite references:
- Issue: https://github.com/J-Tech-Japan/Sekiban/issues/924
- PR: https://github.com/J-Tech-Japan/Sekiban/pull/925

## Runtime-side Goal
When Sekiban introduces MultiProjection primitive abstraction, SekibanWasmRuntime must provide a WASM implementation that is behaviorally compatible with native runtime.

## Scope
- Add MultiProjection WASM primitive implementation matching Sekiban contract.
- Wire runtime DI so host app can select native or wasm for multiprojection.
- Keep existing query projection API behavior stable.

## Expected Runtime Contract (from Sekiban)
Planned core contract shape:
- `IMultiProjectionProjectionPrimitive`
- `IMultiProjectionProjectionAccumulator`

Required capabilities:
- apply snapshot input
- apply serializable events incrementally
- produce serializable snapshot
- expose state metadata (safe/unsafe positions and versions)

## Implementation Plan

### Phase A: Contract sync
1. Update submodule Sekiban to version containing new abstraction.
2. Add compile-time adapter layer in `src/lib/Sekiban.Dcb.WasmRuntime`.
3. Keep build green with feature-flag or temporary fallback if needed.

### Phase B: WASM primitive implementation
1. Add `WasmMultiProjectionProjectionPrimitive` in `src/lib/Sekiban.Dcb.WasmRuntime`.
2. Implement accumulator:
   - `ApplySnapshot(...)`
   - `ApplyEvents(...)` using sorted `SortableUniqueId`
   - `GetSnapshot()`
   - `GetMetadata()`
3. Ensure projector-version mismatch handling is explicit.

### Phase C: Wiring
1. Extend runtime registration extension with explicit MultiProjection primitive registration.
2. Make mode selection deterministic (`native/wasm/hybrid/remote`).
3. Ensure unresolved dependencies fail fast with clear error message.

### Phase D: Verification
1. Add parity tests for:
   - init from empty
   - restore from snapshot
   - catch-up increment
   - version mismatch reset
2. Keep existing tests green.
3. Validate build scripts and CI solution.

## Success Criteria
1. Runtime implementation compiles against latest Sekiban abstraction.
2. MultiProjection behavior parity proven by tests.
3. Runtime mode wiring can switch between native and wasm without API changes.
4. No regression in existing C# and Rust internal usage paths.

## Risks
1. Safe/unsafe semantics mismatch with native actor behavior.
2. Snapshot metadata drift causing incorrect catch-up behavior.
3. Mixed mode routing ambiguity if DI registration is duplicated.
