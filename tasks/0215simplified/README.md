# 0215 Simplified Plan for SekibanWasmRuntime

## Goal
Align SekibanWasmRuntime with the latest TagState abstraction in `submodules/Sekiban`, remove partial/duplicated runtime assumptions, and make implementation executable next week without extra design work.

## Scope
- Repository: `J-Tech-Japan/SekibanWasmRuntime`
- Reference Sekiban path: `submodules/Sekiban/dcb/src`
- Main target: TagState primitive runtime alignment
- Secondary target: internal usage wiring and validation for C#/Rust WASM modules

## Key Constraints
- Keep Sekiban public behavior unchanged.
- Treat `SerializableTagState` and `SerializableEvent` as the canonical boundary.
- WASM implementation must follow the same accumulator contract as native (`ApplyState`, `ApplyEvents`, `GetSerializedState`).
- Do not add Orleans-specific grain abstractions in SekibanWasmRuntime.

## Mandatory Preflight (Do Not Start Coding Before This)
- `submodules/Sekiban` must be updated to the latest `origin/main`.
- If `submodules/Sekiban` is behind, new runtime/primitive contracts may not be visible from SekibanWasmRuntime and implementation will diverge.
- Required commands:
```bash
git -C submodules/Sekiban fetch origin
git -C submodules/Sekiban checkout main
git -C submodules/Sekiban pull --ff-only origin main
git -C submodules/Sekiban rev-parse HEAD
```
- Post the resulting SHA in the tracking issue before Phase 1.

## Deliverables
1. Detailed implementation tasks: `tasks/0215simplified/IMPLEMENTATION_TASKS.md`
2. Next-week execution checklist: `tasks/0215simplified/WEEK_NEXT_EXECUTION.md`
3. Tracking issue body template: `tasks/0215simplified/ISSUE_BODY.md`

## Done Criteria
- All tasks in Phase 1-5 are checked.
- WASM runtime path is aligned to latest Sekiban TagState primitive contract.
- Internal usage examples demonstrate runnable end-to-end behavior for both C# and Rust WASM modules.
- Tests listed in `WEEK_NEXT_EXECUTION.md` pass in CI-compatible environment.
