# Next Week Execution Checklist

Use this checklist as the only implementation driver for next week.

## Day 1 (Mon): Contract and Baseline
- [ ] Run baseline SHA capture and post into issue.
- [ ] Complete Phase 0 tasks from `IMPLEMENTATION_TASKS.md`.
- [ ] Produce gap matrix and agree priority order.

Exit criteria:
- clear gap matrix is posted.

## Day 2 (Tue): DI and Runtime Wiring
- [ ] Complete Phase 1 and Phase 2 tasks.
- [ ] Open PR #1: wiring-only changes (no behavior change intended).

Exit criteria:
- runtime selection logic is explicit and reviewable.

## Day 3 (Wed): TagState WASM Primitive Core
- [ ] Implement Phase 3 tasks.
- [ ] Open PR #2: WASM primitive implementation.
- [ ] Include parity notes against native behavior.

Exit criteria:
- accumulator parity confirmed for main scenarios.

## Day 4 (Thu): Internal Usage and Artifacts
- [ ] Complete Phase 4 tasks.
- [ ] Verify both module build paths (C#/Rust).
- [ ] Open PR #3: internal usage/documentation updates.

Exit criteria:
- examples runnable from docs only.

## Day 5 (Fri): Test/CI Stabilization and Merge
- [ ] Complete Phase 5 tasks.
- [ ] Run required command set and capture outputs in PR.
- [ ] Merge or re-scope remaining blockers with explicit follow-up issue.

Exit criteria:
- all required checks pass or blocker issue is created with concrete scope.

## Mandatory Evidence in PR Descriptions
- exact commands executed
- test results summary
- artifact verification (`csharp-weather.wasm`, `rust-weather.wasm`)
- remaining risks and follow-up tasks
