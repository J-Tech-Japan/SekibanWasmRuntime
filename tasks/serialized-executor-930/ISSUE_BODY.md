## Background
Sekiban PR #930 has been merged and introduced the serialized executor contract required for WASM remote execution.

- PR: https://github.com/J-Tech-Japan/Sekiban/pull/930

SekibanWasmRuntime now needs to apply this contract so both C# and Rust client APIs can execute remote commands through a shared serialized boundary.

## Hard Gate (must complete before coding)
`submodules/Sekiban` must be synced to latest `origin/main`.

```bash
git -C submodules/Sekiban fetch origin
git -C submodules/Sekiban checkout main
git -C submodules/Sekiban pull --ff-only origin main
git -C submodules/Sekiban rev-parse HEAD
```

Post both SHAs in this issue before implementation PRs:
- SekibanWasmRuntime SHA (`git rev-parse HEAD`)
- submodules/Sekiban SHA (`git -C submodules/Sekiban rev-parse HEAD`)

## Source of truth
- `tasks/serialized-executor-930/IMPLEMENTATION_GUIDE.md`

## Goal
Adopt `ISerializedSekibanDcbExecutor` contract in SekibanWasmRuntime for remote execution, while keeping local/native mode available.

## Task Checklist
### Phase A: shared contract integration in lib
- [ ] Add serialized contract-aware client abstraction in `src/lib`
- [ ] Add HTTP transport for `GetSerializableTagStateAsync` and `CommitSerializableEventsAsync`
- [ ] Add in-proc adapter for local tests

### Phase B: API service integration
- [ ] Add serialized endpoints in API service
- [ ] Resolve `ISerializedSekibanDcbExecutor` via DI
- [ ] Return clear errors for contract validation failures

### Phase C: C#/Rust client integration
- [ ] C# remote flow sends `SerializedCommitRequest`
- [ ] Rust remote flow sends same JSON contract
- [ ] consistency tags are explicit and deterministic on both paths

### Phase D: tests and docs
- [ ] Add contract tests (duplicate/unknown consistency tags)
- [ ] Add integration tests (commit success/conflict + get serializable state)
- [ ] Document local vs remote execution model and commands

## Acceptance Criteria
- [ ] submodule sync proof (SHA) posted in issue
- [ ] C# and Rust remote paths both run with serialized contract
- [ ] API service endpoints are wired to `ISerializedSekibanDcbExecutor`
- [ ] tests pass in CI-compatible environment

## Out of scope
- changing Sekiban public API from this repository
- Orleans grain redesign in this repository
