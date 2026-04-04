# Event Sourcing Runtime Benchmark Report

This document tracks the current benchmark status for the Sekiban sample runtimes after the 2026-04-04 300K verification pass.

The current `300,000` event matrix now has fresh reruns for:

- C# WASM
- Rust WASM
- MoonBit WASM

Native C# was rerun on current `main`, but reservation throughput degraded badly during the `300K` pass and the run was intentionally aborted once the slowdown pattern was clear. The historical `native-300k.json` file is still kept as a reference, but it is no longer a trustworthy "good" baseline because it completed with large reservation error counts.

## Runtime Matrix

| Runtime | Host Path | Client API | WASM Module | Module Size |
|---|---|---|---|---|
| C# Native | `submodules/Sekiban/templates/Sekiban.Dcb.Templates/content/Sekiban.Dcb.Orleans.Decider` | ASP.NET Core | N/A | N/A |
| C# WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm` | ASP.NET Core proxy | `sekiban-dcb-decider.wasm` | ~35 MB |
| Rust WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs` | Rust/Axum proxy | `sekiban-dcb-decider-rust.wasm` | ~728 KB |
| MoonBit WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb` | Node proxy | `sekiban-dcb-decider-moonbit.wasm` | ~355 KB |

There is currently no C-language WASM sample in this repository. No `C WASM` row is benchmarked below because there is no corresponding AppHost, module, or build pipeline under `src/samples/` or `src/wasm-projectors/`.

The C# WASM sample AppHost is now treated as WASM-only. It launches `wasmserver` and `clientapi` only, and the sample-native `SekibanDcbDecider.ApiService` source has been removed from this tree. Native benchmark and stability runs should start from the Sekiban template AppHost directly, not from `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm`.

## Benchmark Method

- Tool: `benchmarks/Sekiban.Benchmark.Cli`
- Scenario split:
  - Setup
  - Weather bulk write: 60%
  - Reservation lifecycle: 40%
  - Query performance: 250 queries
- Concurrency: `8`
- Target events: `300,000` for the current matrix, `30,000` for the earlier comparable rerun
- Database: Aspire-managed PostgreSQL
- Storage provider: `postgres`

All three WASM AppHosts now use the same WasmServer tuning:

- `SEKIBAN_WASM_CATCHUP_CONCURRENCY=4`
- `SEKIBAN_WASM_MULTIPROJECTION_CATCHUP_BATCH_SIZE=250`
- `SEKIBAN_WASM_AUTO_COMPACTION_INTERVAL_EVENTS=20000`
- `SEKIBAN_WASM_FORCE_COMPACTING_GC_AFTER_COMPACTION=true`
- `SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB=192`
- `WASM_RUNTIME_ALLOWED_TAG_EVENT_TYPES__RoomProjector=RoomCreated,RoomUpdated,RoomDeactivated,RoomReactivated`

The last setting is important for the C# WASM fix: it prevents `RoomProjector` cached tag-state replay from reapplying reservation-tagged events that do not belong to room-state evolution.

## 2026-04-04 Fresh 300K Matrix

The table below uses fresh `300,000` event runs from current `main` where available.

| Runtime | Status | Weather events/sec | Reservation events/sec | Reservation ops/sec | Query ops/sec | Total wall-clock | Peak RSS | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| C# Native | `stalled on current main` | `1910.0` before stall | `133 -> 32` interval trend before abort | `770 -> 182` interval trend before abort | `not reached` | `aborted during reservation phase` | `~1637.2 MB` | `0` before abort |
| C# WASM | `completed` | `1355.3` | `1882.1` | `723.8` | `115.1` | `199.4 s` | `~2594.2 MB` | `0` |
| Rust WASM | `completed` | `1565.3` | `561.0` | `215.7` | `754.0` | `329.8 s` | `~1312.7 MB` | `0` |
| MoonBit WASM | `completed` | `1460.5` | `556.8` | `214.1` | `164.5` | `340.9 s` | `~1301.6 MB` | `0` |
| C WASM | `not implemented in repo` | `n/a` | `n/a` | `n/a` | `n/a` | `n/a` | `n/a` | `n/a` |

Latency summary:

| Runtime | Weather p50 / p95 | Reservation p50 / p95 | Query p50 / p95 |
|---|---|---|---|
| C# Native | `3.9 / 5.8 ms` before stall | `not completed` | `not reached` |
| C# WASM | `5.7 / 7.8 ms` | `10.0 / 14.0 ms` | `4.2 / 22.9 ms` |
| Rust WASM | `4.9 / 6.7 ms` | `36.4 / 64.8 ms` | `0.8 / 1.5 ms` |
| MoonBit WASM | `5.3 / 7.3 ms` | `38.7 / 61.2 ms` | `2.0 / 22.1 ms` |

Current takeaways:

- C# WASM remains the fastest command path at `300K` among the runtimes that completed successfully, and it still stays under the original `4 GB` memory target.
- Rust WASM and MoonBit WASM now both complete `300K` with no errors and with very similar command-side throughput, around `557-561` reservation events/sec.
- Rust WASM has by far the best query throughput at `300K`, reaching `754 ops/sec`.
- MoonBit WASM is materially slower on query throughput than Rust WASM, but still clearly faster than C# WASM on the query phase.
- Native C# is the outlier on current `main`: reservation throughput collapses during the `300K` run even before errors appear. This is a real regression signal, not a timeout artifact.

### Native C# 300K Stall Note

The current native rerun against the Sekiban template AppHost did not complete. It behaved as follows:

- weather phase completed normally at `1910 events/sec`
- reservation phase started at `133 events/sec` interval throughput
- reservation interval throughput decayed steadily to `32 events/sec`
- the run was stopped after only `12,995 / 120,000` reservation events had been produced
- peak sampled RSS before abort was `~1637.2 MB`

This differs from the historical `benchmarks/results/native-300k.json` file, which did complete, but only by emitting `73,156` reservation errors. The conclusion is the same in both cases: native C# does not currently have an acceptable `300K` reservation profile.

## 2026-04-03 Fresh Comparable 30K Rerun

The native row below now comes from a direct run against the Sekiban template AppHost. It no longer uses the mixed `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm` host.

Throughput table:

| Runtime | Weather events/sec | Reservation events/sec | Query ops/sec | Total wall-clock | Errors |
|---|---:|---:|---:|---:|---:|
| C# Native | `1855.5` | `1719.6` | `1723.6` | `17.2 s` | `0` |
| C# WASM | `1329.6` | `1907.2` | `38.2` | `26.5 s` | `0` |
| Rust WASM | `1336.8` | `1111.6` | `702.1` | `25.1 s` | `0` |
| MoonBit WASM | `1284.1` | `1020.0` | `151.9` | `27.9 s` | `0` |

Latency table:

| Runtime | Weather p50 / p95 | Reservation p50 / p95 | Query p50 / p95 |
|---|---|---|---|
| C# Native | `4.1 / 5.7 ms` | `11.4 / 15.7 ms` | `0.2 / 0.4 ms` |
| C# WASM | `5.8 / 7.8 ms` | `10.2 / 14.0 ms` | `13.3 / 52.9 ms` |
| Rust WASM | `5.7 / 8.2 ms` | `19.6 / 25.1 ms` | `0.8 / 1.7 ms` |
| MoonBit WASM | `6.0 / 8.4 ms` | `21.9 / 27.9 ms` | `2.5 / 22.9 ms` |

Observed peak RSS:

| Runtime | Peak RSS | Note |
|---|---:|---|
| C# Native | `not remeasured` | the template-direct separation pass reran throughput only; the older mixed-host follow-up had `~1879.6 MB` peak RSS |
| C# WASM | `~2041.3 MB` | sampled during `cs-wasm-30k-20260403-fixed-rss` |
| Rust WASM | `~558.0 MB` | sampled during `rs-wasm-30k-20260403` |
| MoonBit WASM | `~545.6 MB` | sampled during `mb-wasm-30k-20260403` |

### 30K Takeaways

- The major C# WASM reservation regression is fixed. Reservation throughput improved from `60.6` events/sec on the earlier 2026-04-03 baseline to `1907.2` events/sec on the current run.
- C# WASM no longer fails reservation progression by corrupting `RoomProjector` tag-state after reservation writes.
- Native C# remains the strongest overall implementation at `30K`, but that does not carry over to the current `300K` run.
- Rust WASM currently has the best query throughput by a large margin.
- Rust WASM and MoonBit WASM still keep the smallest RSS footprints, both around `0.55 GB`.
- C# WASM now fits comfortably under the `4 GB` goal, but query performance is still the weakest measured path.

## C# WASM Regression Fixes Applied

The following changes were applied before the 2026-04-03 rerun:

1. `RemoteCommandContext` now caches tag-state and latest-sortable lookups within a single remote command execution.
2. `QuickReservationWorkflow` no longer issues a full room-list read before every quick reservation.
3. `CreateQuickReservation` and `CreateReservationDraft` no longer perform redundant room existence lookups before loading room state.
4. `KnownTagExistenceProbe` replaces the unsafe direct tracker-only fast path so tag existence is backfilled from the actor layer when needed.
5. `SharedTagStateProcessor` now supports projector-scoped allowlists for delta replay, and `RoomProjector` is restricted to room lifecycle events only.
6. The C#, Rust, and MoonBit AppHosts all pass the same `RoomProjector` allowlist into WasmServer.

### C# WASM Before / After

This compares the broken 2026-04-03 baseline (`cs-wasm-30k-20260403`) with the fixed run (`cs-wasm-30k-20260403-fixed-rss`):

| Metric | Before | After |
|---|---:|---:|
| Reservation events/sec | `60.6` | `1907.2` |
| Reservation p95 | `620.6 ms` | `14.0 ms` |
| Total wall-clock | `213.7 s` | `26.5 s` |
| Peak RSS | `~2536.1 MB` | `~2041.3 MB` |
| Reservation errors | `0` | `0` |

The earlier failed optimization attempts from the same day (`cs-wasm-30k-20260403-opt` and `cs-wasm-30k-20260403-cache`) showed why the final fix needed projector-aware filtering:

- they produced very low reported reservation event throughput because most reservation operations failed immediately
- they emitted `7980` reservation errors
- they did not preserve valid room-state after reservation-tagged writes

## Remaining Bottlenecks

- C# WASM query throughput is still far below Rust WASM and still below MoonBit WASM. The main remaining gap is query-side state access rather than reservation command execution.
- Rust WASM and MoonBit WASM now complete `300K` cleanly, but their reservation lifecycle throughput trails the fixed C# WASM path by roughly `3.4x`.
- Native C# now has the highest-priority scalability issue in this repo. The `300K` reservation path needs profiling before it can be treated as a valid baseline again.

## Result Files

### Current 2026-04-04 300K reruns

- `benchmarks/results/cs-wasm-300k-20260404.json`
- `benchmarks/results/cs-wasm-300k-20260404-rss.log`
- `benchmarks/results/rs-wasm-300k-20260404.json`
- `benchmarks/results/rs-wasm-300k-20260404-rss.log`
- `benchmarks/results/mb-wasm-300k-20260404.json`
- `benchmarks/results/mb-wasm-300k-20260404-rss.log`
- `benchmarks/results/native-300k-20260404.benchmark.log`
- `benchmarks/results/native-300k-20260404-rss.log`

### Current 2026-04-03 comparable 30K reruns

- `benchmarks/results/native-template-30k-20260403.json`
- `benchmarks/results/cs-wasm-30k-20260403-fixed.json`
- `benchmarks/results/cs-wasm-30k-20260403-fixed-rss.json`
- `benchmarks/results/cs-wasm-30k-20260403-fixed-rss.log`
- `benchmarks/results/rs-wasm-30k-20260403.json`
- `benchmarks/results/rs-wasm-30k-20260403-rss.log`
- `benchmarks/results/mb-wasm-30k-20260403.json`
- `benchmarks/results/mb-wasm-30k-20260403-rss.log`

### Historical regression reference files

- `benchmarks/results/native-300k.json`
- `benchmarks/results/rs-wasm-300k.json`
- `benchmarks/results/native-30k-20260403-rerun.json`
- `benchmarks/results/native-30k-20260403-rerun-rss.log`
- `benchmarks/results/cs-wasm-30k-20260402.json`
- `benchmarks/results/mb-wasm-30k-20260402.json`
- `benchmarks/results/cs-wasm-30k-20260403.json`
- `benchmarks/results/cs-wasm-30k-20260403-rss.log`
- `benchmarks/results/cs-wasm-30k-20260403-opt.json`
- `benchmarks/results/cs-wasm-30k-20260403-opt-rss.log`
- `benchmarks/results/cs-wasm-30k-20260403-cache.json`
- `benchmarks/results/cs-wasm-30k-20260403-cache-rss.log`
- `benchmarks/results/cs-wasm-300k-20260403-split-clean.json`
- `benchmarks/results/cs-wasm-300k-20260403-split-rss.log`
- `benchmarks/results/cs-wasm-300k-20260403.json`
- `benchmarks/results/cs-wasm-300k-20260403-rss.log`

## How To Reproduce

For one-command local reproduction, use `scripts/run-benchmark-runtime.sh`.

### C# Native

```bash
./scripts/run-benchmark-runtime.sh native 300000 \
  benchmarks/results/native-300k-local.json \
  benchmarks/results/native-300k-local-rss.log
```

### C# WASM

```bash
./scripts/run-benchmark-runtime.sh cs-wasm 300000 \
  benchmarks/results/cs-wasm-300k-local.json \
  benchmarks/results/cs-wasm-300k-local-rss.log
```

### Rust WASM

```bash
./scripts/run-benchmark-runtime.sh rs-wasm 300000 \
  benchmarks/results/rs-wasm-300k-local.json \
  benchmarks/results/rs-wasm-300k-local-rss.log
```

### MoonBit WASM

```bash
./scripts/run-benchmark-runtime.sh mb-wasm 300000 \
  benchmarks/results/mb-wasm-300k-local.json \
  benchmarks/results/mb-wasm-300k-local-rss.log
```
