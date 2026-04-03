# Event Sourcing Runtime Benchmark Report

This document tracks the current benchmark status for the Sekiban sample runtimes after the 2026-04-03 optimization pass.

The main comparable data set is now the fresh `30,000` event rerun from 2026-04-03 across:

- C# Native
- C# WASM
- Rust WASM
- MoonBit WASM

Older 2026-04-02 and early 2026-04-03 result files are still kept under `benchmarks/results/` as historical references, especially for the pre-fix C# WASM regression.

## Runtime Matrix

| Runtime | Host Path | Client API | WASM Module | Module Size |
|---|---|---|---|---|
| C# Native | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm` | ASP.NET Core | N/A | N/A |
| C# WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm` | ASP.NET Core proxy | `sekiban-dcb-decider.wasm` | ~35 MB |
| Rust WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs` | Rust/Axum proxy | `sekiban-dcb-decider-rust.wasm` | ~728 KB |
| MoonBit WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb` | Node proxy | `sekiban-dcb-decider-moonbit.wasm` | ~355 KB |

## Benchmark Method

- Tool: `benchmarks/Sekiban.Benchmark.Cli`
- Scenario split:
  - Setup
  - Weather bulk write: 60%
  - Reservation lifecycle: 40%
  - Query performance: 250 queries
- Concurrency: `8`
- Target events: `30,000`
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

## 2026-04-03 Fresh Comparable 30K Rerun

Throughput table:

| Runtime | Weather events/sec | Reservation events/sec | Query ops/sec | Total wall-clock | Errors |
|---|---:|---:|---:|---:|---:|
| C# Native | `1891.5` | `1599.2` | `241.5` | `18.3 s` | `0` |
| C# WASM | `1329.6` | `1907.2` | `38.2` | `26.5 s` | `0` |
| Rust WASM | `1336.8` | `1111.6` | `702.1` | `25.1 s` | `0` |
| MoonBit WASM | `1284.1` | `1020.0` | `151.9` | `27.9 s` | `0` |

Latency table:

| Runtime | Weather p50 / p95 | Reservation p50 / p95 | Query p50 / p95 |
|---|---|---|---|
| C# Native | `4.0 / 5.8 ms` | `11.8 / 16.9 ms` | `1.2 / 6.0 ms` |
| C# WASM | `5.8 / 7.8 ms` | `10.2 / 14.0 ms` | `13.3 / 52.9 ms` |
| Rust WASM | `5.7 / 8.2 ms` | `19.6 / 25.1 ms` | `0.8 / 1.7 ms` |
| MoonBit WASM | `6.0 / 8.4 ms` | `21.9 / 27.9 ms` | `2.5 / 22.9 ms` |

Observed peak RSS:

| Runtime | Peak RSS | Note |
|---|---:|---|
| C# Native | `~1879.6 MB` | sampled in a follow-up native RSS run; that run had `8` transient reservation 500s, so throughput comparisons above use the clean rerun |
| C# WASM | `~2041.3 MB` | sampled during `cs-wasm-30k-20260403-fixed-rss` |
| Rust WASM | `~558.0 MB` | sampled during `rs-wasm-30k-20260403` |
| MoonBit WASM | `~545.6 MB` | sampled during `mb-wasm-30k-20260403` |

### Current Takeaways

- The major C# WASM reservation regression is fixed. Reservation throughput improved from `60.6` events/sec on the earlier 2026-04-03 baseline to `1907.2` events/sec on the current run.
- C# WASM no longer fails reservation progression by corrupting `RoomProjector` tag-state after reservation writes.
- Native C# remains the fastest overall implementation because its weather and query paths are still much stronger than the WASM variants.
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

- C# WASM query throughput is still far below native and Rust WASM. The main remaining gap is query-side state access rather than reservation command execution.
- Rust WASM and MoonBit WASM now look memory-efficient, but their reservation lifecycle throughput trails the fixed C# WASM path.
- Native C# had one noisy follow-up RSS rerun with `8` transient reservation `500` responses; the clean throughput rerun did not reproduce that. This needs separate investigation if native stability becomes a priority.

## Result Files

### Current 2026-04-03 comparable reruns

- `benchmarks/results/native-30k-20260403-rerun.json`
- `benchmarks/results/native-30k-20260403-rerun-rss.log`
- `benchmarks/results/cs-wasm-30k-20260403-fixed.json`
- `benchmarks/results/cs-wasm-30k-20260403-fixed-rss.json`
- `benchmarks/results/cs-wasm-30k-20260403-fixed-rss.log`
- `benchmarks/results/rs-wasm-30k-20260403.json`
- `benchmarks/results/rs-wasm-30k-20260403-rss.log`
- `benchmarks/results/mb-wasm-30k-20260403.json`
- `benchmarks/results/mb-wasm-30k-20260403-rss.log`

### Historical regression reference files

- `benchmarks/results/cs-wasm-30k-20260402.json`
- `benchmarks/results/mb-wasm-30k-20260402.json`
- `benchmarks/results/cs-wasm-30k-20260403.json`
- `benchmarks/results/cs-wasm-30k-20260403-rss.log`
- `benchmarks/results/cs-wasm-30k-20260403-opt.json`
- `benchmarks/results/cs-wasm-30k-20260403-opt-rss.log`
- `benchmarks/results/cs-wasm-30k-20260403-cache.json`
- `benchmarks/results/cs-wasm-30k-20260403-cache-rss.log`

## How To Reproduce

### C# Native

```bash
dotnet run --project src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj

dotnet run --project benchmarks/Sekiban.Benchmark.Cli/Sekiban.Benchmark.Cli.csproj -- \
  --base-url http://127.0.0.1:5141 \
  --mode-label native-30k-local \
  --total-events 30000 \
  --concurrency 8 \
  --output benchmarks/results/native-30k-local.json
```

### C# WASM

```bash
dotnet run --project src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj

dotnet run --project benchmarks/Sekiban.Benchmark.Cli/Sekiban.Benchmark.Cli.csproj -- \
  --base-url http://127.0.0.1:5198 \
  --mode-label cs-wasm-30k-local \
  --total-events 30000 \
  --concurrency 8 \
  --output benchmarks/results/cs-wasm-30k-local.json
```

### Rust WASM

```bash
dotnet run --project src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj

dotnet run --project benchmarks/Sekiban.Benchmark.Cli/Sekiban.Benchmark.Cli.csproj -- \
  --base-url http://127.0.0.1:6198 \
  --mode-label rs-wasm-30k-local \
  --total-events 30000 \
  --concurrency 8 \
  --output benchmarks/results/rs-wasm-30k-local.json
```

### MoonBit WASM

```bash
dotnet run --project src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj

dotnet run --project benchmarks/Sekiban.Benchmark.Cli/Sekiban.Benchmark.Cli.csproj -- \
  --base-url http://127.0.0.1:6198 \
  --mode-label mb-wasm-30k-local \
  --total-events 30000 \
  --concurrency 8 \
  --output benchmarks/results/mb-wasm-30k-local.json
```
