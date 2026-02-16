## Goal
Align SekibanWasmRuntime to latest Sekiban TagState abstraction and remove partial runtime assumptions so next-week implementation can proceed directly.

## References
- `tasks/0215simplified/README.md`
- `tasks/0215simplified/IMPLEMENTATION_TASKS.md`
- `tasks/0215simplified/WEEK_NEXT_EXECUTION.md`
- `tasks/cswasm/IMPLEMENTATION_GUIDE5.md`
- `submodules/Sekiban/dcb/src/Sekiban.Dcb.Core/Runtime/ITagStateProjectionPrimitive.cs`
- `submodules/Sekiban/dcb/src/Sekiban.Dcb.Orleans.Core/Grains/TagStateGrain.cs`

## Scope
- TagState primitive/runtime alignment
- runtime wiring cleanup for native/wasm/hybrid/remote modes
- internal usage examples for C# and Rust WASM
- tests and CI verification for regression control

## Mandatory Preflight (must be completed first)
- Update `submodules/Sekiban` to latest `origin/main`:
```bash
git -C submodules/Sekiban fetch origin
git -C submodules/Sekiban checkout main
git -C submodules/Sekiban pull --ff-only origin main
git -C submodules/Sekiban rev-parse HEAD
```
- Why this is required:
  - If submodule is not at latest `main`, newly added runtime/primitive abstractions may be missing and SekibanWasmRuntime cannot correctly reference the target contracts.
- Do not start implementation PRs until this SHA is posted in this issue.

## Task Checklist
### Phase 0 Baseline lock
- [ ] Sync `submodules/Sekiban` to latest `origin/main` and post SHA
- [ ] Capture and post repository SHAs (SekibanWasmRuntime + submodules/Sekiban)
- [ ] Verify latest Sekiban TagState primitive contract and usage

### Phase 1 Contract alignment
- [ ] Produce gap matrix (contract vs current status vs fix path)
- [ ] Map runtime usage points to keep/replace/remove actions

### Phase 2 Wiring refactor
- [ ] Introduce explicit DI registration path for TagState primitive runtime
- [ ] Normalize runtime mode selection and fail-fast behavior
- [ ] Add minimal non-obvious wiring comments

### Phase 3 WASM primitive implementation
- [ ] Implement WASM accumulator (`ApplyState`, `ApplyEvents`, `GetSerializedState`)
- [ ] Enforce projector version and identity guards
- [ ] Ensure error semantics parity with native expectation

### Phase 4 Internal usage examples
- [ ] Validate/update C# WASM path and regeneration command
- [ ] Validate/update Rust WASM path and regeneration command
- [ ] Validate runner/API integration examples for local/remote command paths

### Phase 5 Tests and CI verification
- [ ] Add/adjust tests for init, catch-up, restore same version, restore changed version
- [ ] Verify runtime selection tests (native/wasm/hybrid/remote)
- [ ] Run required command set and attach results

## Success Criteria
- WASM and native produce equivalent TagState outputs for key fields:
  - `Version`
  - `LastSortedUniqueId`
  - `TagPayloadName`
  - `ProjectorVersion`
- Internal usage examples are runnable from documentation only.
- CI command set completes successfully.

## Out of Scope
- changing Sekiban public API contracts from this repository
- Orleans-specific custom grain design in SekibanWasmRuntime
