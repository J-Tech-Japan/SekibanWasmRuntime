# Event Sourcing Runtime Benchmark Report

This document tracks two benchmark profiles:

- the stricter 2026-04-08 `tagstategrain-memory` rerun, which disables shortcut paths and routes tag-state through Orleans `TagStateGrain`
- the older optimized `300,000` event matrix from the 2026-04-04 pass, kept as a shortcut-enabled baseline

When the two profiles disagree, treat the 2026-04-08 strict profile as the current answer for all six implemented runtimes. The older C# WASM row (`1355.3 weather eps`, `1882.1 reservation eps`, `115.1 query ops/sec`, `~2594.2 MB`) is not the latest strict result; it is the earlier shortcut-enabled baseline.

The current `300,000` event matrix now has fresh reruns for:

- Native C#
- C# WASM
- Rust WASM
- MoonBit WASM
- Go WASM
- TypeScript WASM

## Runtime Matrix

| Runtime | Host Path | Client API | WASM Module | Module Size |
|---|---|---|---|---|
| C# Native | `submodules/Sekiban/templates/Sekiban.Dcb.Templates/content/Sekiban.Dcb.Orleans.Decider` | ASP.NET Core | N/A | N/A |
| C# WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm` | ASP.NET Core proxy | `sekiban-dcb-decider.wasm` | ~35 MB |
| Rust WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs` | Rust/Axum proxy | `sekiban-dcb-decider-rust.wasm` | ~728 KB |
| MoonBit WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb` | Node proxy | `sekiban-dcb-decider-moonbit.wasm` | ~355 KB |
| Go WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Go` | Go/net-http proxy | `go-weather.wasm` | ~1.3 MB |
| TypeScript WASM | `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts` | Hono/Node proxy | `ts-weather.wasm` | ~239 KB |

There is currently no C-language WASM sample in this repository. No `C WASM` row is benchmarked below because there is no corresponding AppHost, module, or build pipeline under `src/samples/` or `src/wasm-projectors/`.

The C# WASM sample AppHost is now treated as WASM-only. It launches `wasmserver` and `clientapi` only, and the sample-native `SekibanDcbDecider.ApiService` source has been removed from this tree. Native benchmark and stability runs should start from the Sekiban template AppHost directly, not from `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm`.

## MoonBit Shared Library Migration

As of 2026-04-07, MoonBit WASM now uses shared libraries extracted to `src/lib/sekiban-moonbit/`:

- **wasm-runtime** (`src/lib/sekiban-moonbit/wasm-runtime/`): FFI, memory management, generic types, projector callback registry, WASM exports, query helpers
- **client** (`src/lib/sekiban-moonbit/client/`): HTTP client (`SekibanRuntimeClient`), executor (`StaticTagProjectorResolver`, `finalize_command`), server framework (router, handlers)

The domain sample at `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/moonbit/runtime/` now imports these shared libraries via path dependencies. A verification 5K benchmark confirms the shared-library build produces identical runtime behavior to the previous monolithic build (0 errors, comparable throughput).

A full 300K benchmark was also run to validate at scale:

| Metric | Shared-lib 300K | Previous 300K (monolithic) |
|---|---:|---:|
| Weather events/sec | `1479` | `1461` |
| Reservation events/sec | `580` | `557` |
| Reservation ops/sec | `223` | `214` |
| Query ops/sec | `4` | `165` |
| Total wall-clock | `427.0 s` | `340.9 s` |
| Peak RSS | `~1389 MB` | `~1302 MB` |
| Errors | `0` | `0` |

Command-side throughput is comparable or slightly improved after the shared library migration. Query throughput was lower in this run, likely due to host load variability rather than the migration itself. The WASM module size remains ~358 KB (release build), unchanged from the pre-migration build.

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

All WASM AppHosts now use the same WasmServer tuning:

- `SEKIBAN_WASM_CATCHUP_CONCURRENCY=4`
- `SEKIBAN_WASM_MULTIPROJECTION_CATCHUP_BATCH_SIZE=250`
- `SEKIBAN_WASM_AUTO_COMPACTION_INTERVAL_EVENTS=20000`
- `SEKIBAN_WASM_FORCE_COMPACTING_GC_AFTER_COMPACTION=true`
- `SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB=192`

Tag-state is now resolved via Orleans `TagStateGrain` instead of the in-process `SharedTagStateProcessor`. The grain provides persistent caching, delta replay, and distributed concurrency management.

## 2026-04-08 Strict `tagstategrain-memory` Profile

This profile was added to answer the question "how fast are the implemented runtimes when we do not shortcut tag-state?" It intentionally removes the fast paths that made the earlier optimized rows look better.

Profile rules:

- use `TagStateGrain` as the authoritative tag-state path
- disable direct snapshot query shortcuts in the C# WASM host
- disable the runtime's tag-state missing-tag fast path in the C# WASM host
- use Orleans in-memory streams and in-memory grain storage for the Native template AppHost
- keep the event store on PostgreSQL so command/query behavior still runs against real persisted events

The strict runs were executed with:

- `scripts/run-benchmark-runtime.sh native 300000 ... tagstategrain-memory`
- `scripts/run-benchmark-runtime.sh cs-wasm 300000 ... tagstategrain-memory`
- `scripts/run-benchmark-runtime.sh rs-wasm 300000 ... tagstategrain-memory`
- `scripts/run-benchmark-runtime.sh mb-wasm 300000 ... tagstategrain-memory`
- `scripts/run-benchmark-runtime.sh go-wasm 300000 ... tagstategrain-memory`
- `scripts/run-benchmark-runtime.sh ts-wasm 300000 ... tagstategrain-memory`

Strict profile wiring:

- Native template AppHost: `Orleans__UseInMemoryStreams=true`, `Orleans__UseInMemoryGrainStorage=true`
- All WASM AppHosts: `SEKIBAN_DIRECT_SNAPSHOT_QUERY_ENABLED=false`, `SEKIBAN_TAG_STATE_FAST_PATH_ENABLED=false`
- Rust / MoonBit / Go / TypeScript sample ApiServices: `Orleans__UseInMemoryStreams=true`, `Orleans__UseInMemoryGrainStorage=true`

### Strict 300K Results

| Runtime | Status | Weather events/sec | Reservation events/sec | Reservation ops/sec | Query ops/sec | Total wall-clock | Peak RSS | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| C# Native (`tagstategrain-memory`) | `completed` | `1965.5` | `747.0` | `287.3` | `1808.6` | `254.2 s` | `~2953.4 MB` | `0` |
| C# WASM (`tagstategrain-memory`) | `completed` | `1581.9` | `254.4` | `97.9` | `942.1` | `586.6 s` | `~3950.6 MB` | `0` |
| Rust WASM (`tagstategrain-memory`) | `completed` | `480.0` | `148.0` | `57.0` | `2081.0` | `1187.9 s` | `~2327.4 MB` | `0` |
| MoonBit WASM (`tagstategrain-memory`) | `completed` | `515.0` | `163.0` | `63.0` | `166.0` | `1089.5 s` | `~2320.5 MB` | `0` |
| Go WASM (`tagstategrain-memory`) | `completed` | `510.0` | `190.0` | `73.0` | `816.0` | `986.6 s` | `~2980.4 MB` | `0` |
| TypeScript WASM (`tagstategrain-memory`) | `completed` | `475.0` | `196.0` | `75.0` | `418.0` | `992.5 s` | `~3999.7 MB` | `0` |

Latency summary:

| Runtime | Weather p50 / p95 | Reservation p50 / p95 | Query p50 / p95 |
|---|---|---|---|
| C# Native (`tagstategrain-memory`) | `3.9 / 5.7 ms` | `11.2 / 87.1 ms` | `0.2 / 0.4 ms` |
| C# WASM (`tagstategrain-memory`) | `4.9 / 6.7 ms` | `73.4 / 138.1 ms` | `0.6 / 1.3 ms` |
| Rust WASM (`tagstategrain-memory`) | `16.5 / 27.6 ms` | `135.5 / 221.0 ms` | `0.2 / 0.6 ms` |
| MoonBit WASM (`tagstategrain-memory`) | `15.7 / 24.3 ms` | `128.6 / 200.1 ms` | `5.7 / 17.9 ms` |
| Go WASM (`tagstategrain-memory`) | `15.9 / 26.0 ms` | `104.4 / 177.7 ms` | `1.0 / 2.4 ms` |
| TypeScript WASM (`tagstategrain-memory`) | `16.3 / 30.0 ms` | `102.0 / 170.0 ms` | `1.5 / 4.9 ms` |

Takeaways from the strict profile:

- All six implemented runtimes now complete the strict `300K` profile with `0` errors.
- Native C# remains the clear strict-profile leader on both command throughput and query throughput.
- Among the non-C# WASM runtimes, Go and TypeScript have the strongest reservation command path under strict conditions, while Rust is the clear query-throughput leader.
- C# WASM and TypeScript WASM both stay under the original `4 GB` guardrail, but only barely at `~3950.6 MB` and `~3999.7 MB`.
- Rust WASM and MoonBit WASM keep the lowest strict-profile memory usage, both near `~2.32 GB`, but they pay for that with the weakest reservation throughput under long runs.
- The biggest strict-profile regression remains the command path which depends on repeated tag-state resolution. Every WASM runtime degrades materially relative to the optimized baseline, but C# WASM still holds a substantial lead over Rust and MoonBit on reservation events/sec.

### C# WASM vs Native C# Strict 300K Investigation

The strict `300K` numbers are valid, but they are not an apples-to-apples "same code plus WASM only" comparison.

- Native C# reservation throughput finishes at `287.3 ops/sec` (`747.0 events/sec`) with reservation p50 / p95 latency `11.2 / 87.1 ms`.
- C# WASM reservation throughput finishes at `97.9 ops/sec` (`254.4 events/sec`) with reservation p50 / p95 latency `73.4 / 138.1 ms`.
- Native reservation interval throughput degrades from `961 eps` at the first progress sample to `576 eps` at the last one.
- C# WASM reservation interval throughput degrades more sharply, from `435 eps` to `168 eps`.

The apphost log shows where that extra time goes for strict C# WASM.

| C# WASM reservation-phase signal | Measured value |
|---|---:|
| Reservation ops | `46,163` |
| Client `/api/reservations/quick` starts | `36,913` |
| Client `/api/reservations/draft` starts | `9,229` |
| WasmServer `/serialized/tag-state` starts | `83,051` |
| WasmServer `/serialized/commit` starts | `46,749` |
| WasmServer `/serialized/tag-latest-sortable` starts | `46,745` |

That means the strict C# WASM reservation path is doing approximately:

- `~1.80` `serialized/tag-state` POSTs per reservation op
- `~1.01` `serialized/commit` POSTs per reservation op
- `~1.01` `serialized/tag-latest-sortable` POSTs per reservation op
- `~2.25` `serialized/tag-state` POSTs per quick reservation

The internal endpoint latencies also drift upward during the run:

| Endpoint | First 5,000 avg | Last 5,000 avg | p50 | p95 |
|---|---:|---:|---:|---:|
| Client `/api/reservations/quick` | `50.4 ms` | `127.7 ms` | `82.8 ms` | `142.0 ms` |
| Client `/api/reservations/draft` | `35.6 ms` | `53.6 ms` | `42.2 ms` | `74.1 ms` |
| WasmServer `/serialized/tag-state` | `20.1 ms` | `56.3 ms` | `32.7 ms` | `68.0 ms` |
| WasmServer `/serialized/commit` | `4.1 ms` | `10.6 ms` | `3.5 ms` | `10.9 ms` |
| WasmServer `/serialized/tag-latest-sortable` | `1.5 ms` | `2.3 ms` | `1.0 ms` | `2.2 ms` |

The practical conclusion is:

- The dominant strict-profile slowdown for C# WASM is the command-side remote path, especially repeated `serialized/tag-state` calls whose latency roughly triples over the run.
- Commit retries are not the main issue. `serialized/commit` is only about `1.3%` above reservation-op count in the reservation phase.
- Query throughput is lower than Native C#, but the biggest strict gap is not query; it is the reservation command path.
- Native strict runs do not pay the same `benchmark -> ClientApi -> WasmServer -> TagStateGrain` hop pattern, so the current strict profile measures architecture plus WASM, not WASM execution in isolation.

If the goal is a fair runtime comparison, the benchmark needs one of these before the next round:

- a Native control path that uses the same serialized remote command flow as C# WASM, or
- a C# WASM host path that executes the same command flow without the extra HTTP proxy hops

The analysis was generated from the saved strict benchmark log with `scripts/analyze-apphost-http-log.py` and written to `benchmarks/results/cs-wasm-300k-tagstategrain-memory-20260408-http-analysis.json`.

### Strict 50K Validation

The `50,000` event smoke runs were used to verify the strict setup before the full `300K` pass.

| Runtime | Weather events/sec | Reservation events/sec | Reservation ops/sec | Query ops/sec | Total wall-clock | Peak RSS | Errors |
|---|---:|---:|---:|---:|---:|---:|---:|
| C# Native (`tagstategrain-memory`) | `1813.0` | `1517.2` | `583.5` | `1995.1` | `30.5 s` | `~894.1 MB` | `0` |
| C# WASM (`tagstategrain-memory`) | `1456.0` | `510.5` | `196.3` | `1008.1` | `60.6 s` | `~1931.2 MB` | `0` |

### Strict Profile Notes

The first strict C# WASM query run was invalid because the checked-in `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/modules/sekiban-dcb-decider.wasm` was stale relative to the source tree. That stale module could not instantiate `RoomListProjection`, which caused `create_instance failed with code -1` during query catch-up.

This was fixed by:

- rebuilding the sample C# WASM module with `build/scripts/build-sample-csharp-wasm.sh`
- copying the fresh output back to `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/modules/sekiban-dcb-decider.wasm`
- making `scripts/run-benchmark-runtime.sh` auto-rebuild the sample module when the source tree is newer than the checked-in module

The strict TypeScript rerun initially failed for a different reproducibility reason: `ts-clientapi` had no local `node_modules`, so `npx tsx` could not resolve `@hono/node-server`. This was fixed by making `scripts/run-benchmark-runtime.sh` auto-run `npm install --no-audit --no-fund` for `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/ts-clientapi` when those dependencies are missing.

Result files:

- `benchmarks/results/native-50k-tagstategrain-memory-20260408.json`
- `benchmarks/results/native-300k-tagstategrain-memory-20260408.json`
- `benchmarks/results/cs-wasm-50k-tagstategrain-memory-20260408-fix2.json`
- `benchmarks/results/cs-wasm-300k-tagstategrain-memory-20260408.json`
- `benchmarks/results/cs-wasm-300k-tagstategrain-memory-20260408-http-analysis.json`
- `benchmarks/results/rs-wasm-300k-tagstategrain-memory-20260408.json`
- `benchmarks/results/mb-wasm-300k-tagstategrain-memory-20260408.json`
- `benchmarks/results/go-wasm-300k-tagstategrain-memory-20260408.json`
- `benchmarks/results/ts-wasm-300k-tagstategrain-memory-20260408.json`

## 2026-04-04 Optimized 300K Baseline

This section is retained for comparison only. It used the shortcut-enabled runtime configuration from the 2026-04-04 pass, so its rows are not the current strict-profile values.

The table below uses fresh `300,000` event runs. The C# Native row shows the PostgreSQL-backed result for apples-to-apples comparison with the WASM runtimes (all use PostgreSQL). The in-memory result is documented separately below.

| Runtime | Status | Weather events/sec | Reservation events/sec | Reservation ops/sec | Query ops/sec | Total wall-clock | Peak RSS | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| C# Native (Postgres) | `completed` | `2004.1` | `236.7` | `91.0` | `1586.5` | `597.6 s` | `~1637.2 MB` | `0` |
| C# Native (in-memory) | `completed` | `1988.7` | `2001.4` | `769.8` | `1759.0` | `151.2 s` | `n/a` | `0` |
| C# WASM | `completed` | `1355.3` | `1882.1` | `723.8` | `115.1` | `199.4 s` | `~2594.2 MB` | `0` |
| Rust WASM | `completed` | `1565.3` | `561.0` | `215.7` | `754.0` | `329.8 s` | `~1312.7 MB` | `0` |
| MoonBit WASM | `completed` | `1460.5` | `556.8` | `214.1` | `164.5` | `340.9 s` | `~1301.6 MB` | `0` |
| Go WASM | `completed` | `1547.8` | `520.1` | `200.0` | `335.8` | `388.5 s` | `~2514.2 MB` | `0` |
| TypeScript WASM | `completed` | `1494.7` | `559.3` | `215.1` | `2.0` | `498.7 s` | `~1724.1 MB` | `0` |
| C WASM | `not implemented in repo` | `n/a` | `n/a` | `n/a` | `n/a` | `n/a` | `n/a` | `n/a` |

Latency summary:

| Runtime | Weather p50 / p95 | Reservation p50 / p95 | Query p50 / p95 |
|---|---|---|---|
| C# Native (Postgres) | `3.8 / 5.4 ms` | `66.0 / 225.5 ms` | `0.2 / 0.4 ms` |
| C# Native (in-memory) | `3.8 / 5.4 ms` | `8.5 / 16.0 ms` | `0.2 / 0.6 ms` |
| C# WASM | `5.7 / 7.8 ms` | `10.0 / 14.0 ms` | `4.2 / 22.9 ms` |
| Rust WASM | `4.9 / 6.7 ms` | `36.4 / 64.8 ms` | `0.8 / 1.5 ms` |
| MoonBit WASM | `5.3 / 7.3 ms` | `38.7 / 61.2 ms` | `2.0 / 22.1 ms` |
| Go WASM | `5.0 / 6.7 ms` | `37.6 / 66.2 ms` | `1.3 / 7.2 ms` |
| TypeScript WASM | `5.2 / 7.1 ms` | `36.8 / 64.6 ms` | `7.1 / 3999.2 ms` |

Current takeaways:

- All six runtimes now complete `300K` with zero errors.
- **C# Native with in-memory storage is the fastest overall runtime**, completing `300K` in just `151 s` with `2001 reservation eps` — an **8.5x improvement** over the PostgreSQL-backed run. This confirms that the native reservation throughput decay was caused by PostgreSQL I/O contention, not by the Sekiban framework itself.
- C# WASM remains the fastest command path among WASM runtimes at `300K`, and it still stays under the original `4 GB` memory target.
- Native C# (Postgres) completes `300K` cleanly and stays under `2 GB`, but its reservation phase still decays from roughly `670 eps` at the start to roughly `125 eps` at the end of the run due to PostgreSQL write amplification.
- Rust WASM, MoonBit WASM, Go WASM, and TypeScript WASM all have very similar command-side throughput, around `520-561` reservation events/sec.
- Rust WASM and MoonBit WASM share the smallest memory footprint at `~1302-1313 MB` and Rust WASM has the best query throughput at `754 ops/sec`.
- Go WASM has strong query performance (`335.8 ops/sec`) but the highest memory usage among WASM runtimes at `~2514 MB`.
- TypeScript WASM has the smallest WASM module size (`~239 KB`) and competitive command throughput, but its query phase is severely degraded (`2.0 ops/sec`, p95 `~4 s`) — likely a cold-start or GC issue in the Hono/Node client API layer.
- MoonBit WASM is materially slower on query throughput than Rust WASM, but still clearly faster than C# WASM on the query phase.

### Native C# 300K Completion Note

The current native rerun against the Sekiban template AppHost now completes with `0` errors:

- weather phase completed at `2004.1 events/sec`
- reservation phase completed at `236.7 events/sec` overall and `91.0 ops/sec`
- reservation interval throughput still decayed steadily during the run, from roughly `673 eps` at the start to roughly `124-140 eps` at the end
- peak sampled RSS was `~1637.2 MB`
- query phase completed at `1586.5 ops/sec`

The previous aborted native runs turned out to mix two different problems:

- the benchmark was under-provisioning rooms for `300K`, so conflict-checked implementations eventually ran out of unique reservation slots
- the native template quick-reservation path and room reservation state were doing more work than necessary per command

That benchmark-side issue is now fixed, so the current native `300K` result is a trustworthy completion result. The remaining issue is throughput decay, not correctness.

### Native C# In-Memory Storage Results

A `skip-persist` run was performed on 2026-04-08 to isolate the impact of PostgreSQL I/O on native throughput. The in-memory storage provider eliminates all database writes and reads, keeping events and projections entirely in process memory.

| Metric | PostgreSQL | In-Memory | Speedup |
|---|---:|---:|---:|
| Weather events/sec | `2004.1` | `1988.7` | `1.0x` |
| Reservation events/sec | `236.7` | `2001.4` | **`8.5x`** |
| Reservation ops/sec | `91.0` | `769.8` | **`8.5x`** |
| Query ops/sec | `1586.5` | `1759.0` | `1.1x` |
| Total wall-clock | `597.6 s` | `151.2 s` | **`3.9x`** |
| Reservation p50 | `66.0 ms` | `8.5 ms` | **`7.8x`** |
| Reservation p95 | `225.5 ms` | `16.0 ms` | **`14.1x`** |

Key observations:
- Weather throughput is unchanged (~2000 eps) because weather events are simple append-only writes with no conflict checking, so PostgreSQL I/O is not the bottleneck.
- Reservation throughput jumps from `237` to `2001` events/sec — the entire `300K` reservation decay pattern disappears. This proves the decay was caused by PostgreSQL write contention on the conflict-checked reservation path.
- The in-memory reservation latency profile (`p50=8.5 ms`, `p95=16.0 ms`) is comparable to the WASM runtimes, confirming that the native Sekiban command pipeline is efficient once database I/O is removed.
- Query throughput improves marginally (`1587` → `1759` ops/sec) because query reads no longer hit PostgreSQL.

This result establishes the **theoretical upper bound** for native C# performance. Any future PostgreSQL optimization work should aim to close the gap between `237 eps` (Postgres) and `2001 eps` (in-memory) on the reservation path.

### Native C# Optimization Series (2026-04-07)

A series of incremental PostgreSQL-side optimizations were tested before the in-memory run:

| Run | Reservation eps | Total time | Change |
|---|---:|---:|---|
| Baseline (04/04) | `236.7` | `597.6 s` | — |
| delta-fix | `240.9` | `586.0 s` | +1.7% |
| tags-join | `248.6` | `573.6 s` | +5.0% |
| with-logs | `260.0` | `552.4 s` | +9.9% |
| local-ref | `262.1` | `547.8 s` | +10.7% |
| **skip-persist (in-memory)** | **`2001.4`** | **`151.2 s`** | **+745%** |

The incremental Postgres optimizations (delta-fix through local-ref) yielded a cumulative ~11% improvement on reservation throughput. The in-memory result shows the remaining ~8x gap is entirely due to PostgreSQL I/O overhead.

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

## Native C# Fixes Applied For 300K Rerun

The current native template rerun includes both benchmark-side and Sekiban template-side fixes:

1. `SetupScenario` now provisions enough rooms for the reservation event target instead of assuming the old fixed `20`-room setup.
2. `HttpApiClient` now retries native auth endpoints during startup, so fresh AppHost launches do not lose the setup phase to transient `401` responses.
3. `QuickReservationWorkflow` now uses a single `CreateQuickReservation` command instead of executing draft, hold, and confirm as separate round trips.
4. `RoomReservationTag` separates room reservation conflict tracking from `RoomTag`, so room metadata state is no longer polluted by all reservation writes.
5. `RoomReservationsState` now mutates in place and keeps a day index, reducing per-command copying and limiting conflict scans to relevant day buckets.
6. `ReservationListProjection` and `ApprovalRequestListProjection` now update their dictionaries in place instead of cloning the full map for every event.

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

- **Native C# (Postgres) reservation decay** is now fully explained: the in-memory run proves the `8.5x` gap is PostgreSQL I/O contention. Future optimization should focus on batching, connection pooling, or event store write path improvements.
- C# WASM query throughput is still far below Rust WASM and still below MoonBit WASM. The main remaining gap is query-side state access rather than reservation command execution.
- Rust WASM, MoonBit WASM, Go WASM, and TypeScript WASM now all complete `300K` cleanly, but their reservation lifecycle throughput trails the fixed C# WASM path by roughly `3.4x`.
- TypeScript WASM query throughput is extremely low (`2.0 ops/sec`) at `300K`. The command phase performs well, so the bottleneck is likely in the Hono/Node client API query path or garbage collection pauses under high state volume.
- Go WASM has the highest memory usage among WASM runtimes (`~2514 MB`), close to C# WASM. The TinyGo runtime and linear memory growth likely account for this.

## Result Files

### 2026-04-07 Shared Library Migration Verification (5K + 300K)

- `benchmarks/results/mb-wasm-5k-shared-lib.json`
- `benchmarks/results/mb-wasm-5k-shared-lib-rss.log`
- `benchmarks/results/mb-wasm-300k-shared-lib.json`
- `benchmarks/results/mb-wasm-300k-shared-lib-rss.log`

### 2026-04-07 Native C# Optimization Series

- `benchmarks/results/native-300k-delta-fix.json` / `-rss.log` (delta-fix optimization)
- `benchmarks/results/native-300k-tags-join.json` / `-rss.log` (tags-join optimization)
- `benchmarks/results/native-300k-with-logs.json` / `-rss.log` (with-logs optimization)
- `benchmarks/results/native-300k-local-ref.json` (local-ref optimization)
- `benchmarks/results/native-300k-skip-persist.json` (in-memory storage — no RSS log)

### Current 2026-04-04 300K reruns

- `benchmarks/results/native-300k-20260404-fix10.json` / `-rss.log` (2026-04-04)
- `benchmarks/results/cs-wasm-300k-20260404.json` / `-rss.log` (2026-04-04)
- `benchmarks/results/rs-wasm-300k-20260404.json` / `-rss.log` (2026-04-04)
- `benchmarks/results/mb-wasm-300k-20260404.json` / `-rss.log` (2026-04-04)
- `benchmarks/results/go-wasm-300k.json` / `-rss.log` (2026-04-07)
- `benchmarks/results/ts-wasm-300k.json` / `-rss.log` (2026-04-07)

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

### Go WASM

```bash
./scripts/run-benchmark-runtime.sh go-wasm 300000 \
  benchmarks/results/go-wasm-300k-local.json \
  benchmarks/results/go-wasm-300k-local-rss.log
```

### TypeScript WASM

```bash
./scripts/run-benchmark-runtime.sh ts-wasm 300000 \
  benchmarks/results/ts-wasm-300k-local.json \
  benchmarks/results/ts-wasm-300k-local-rss.log
```
