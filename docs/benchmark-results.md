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

The first strict C# WASM numbers from 2026-04-08 were materially wrong for long-run reservation throughput. The root cause was a WASM-host metadata bug, not "WASM is inherently 2x slower".

- `ManifestDomainTypes` was building `DcbDomainTypes` with an empty `AotTagProjectorTypes()`
- `TagStateGrain` therefore resolved the expected projector version as `""` in the WASM host
- the accumulator still wrote cached tag-state with manifest version `1.0.0`
- every subsequent read hit `version-mismatch`, so the grain rebuilt state instead of using delta replay

Fixes applied on 2026-04-09:

- `src/runtime/Sekiban.Dcb.WasmRuntime.Host/ManifestDomainTypes.cs` now uses a manifest-backed `ITagProjectorTypes`
- `src/runtime/Sekiban.Dcb.WasmRuntime.Host/ManifestTagProjectorTypes.cs` provides projector-version lookup from the manifest
- `src/lib/Sekiban.Dcb.WasmRuntime/WasmTagStateProjectionPrimitive.cs` now falls back to host-side metadata when the WASM state serializer returns blank projector/tag metadata
- `scripts/run-benchmark-runtime.sh` now ignores `obj/` and `bin/` noise when deciding whether the checked-in C# WASM module is stale

Evidence from the 1K diagnostics:

| C# WASM diagnostic | Before fix | After fix |
|---|---:|---:|
| `RoomProjector` version mismatch count | `80` | `0` |
| `RoomReservationsProjector` version mismatch count | `84` | `0` |
| `RoomProjector` avg events read | `3.00` | `1.00` |
| `RoomReservationsProjector` avg events read | `5.28` | `1.68` |

Native-vs-WASM fairness note:

- the Native template `CreateReservationDraft` reads `UserAccessProjector`, `UserDirectoryProjector`, and `UserMonthlyReservationProjector`
- the C# WASM sample `CreateReservationDraft` does not execute those extra user-governance reads
- the original mixed strict benchmark therefore measured "latest package Native template behavior" against a lighter C# WASM draft path
- the Rust / MoonBit / Go / TypeScript WASM command implementations already matched the lighter draft path, so their strict rows were intended to be runtime-comparable to C# WASM

To isolate runtime overhead, the latest Native reruns use:

- `BENCHMARK_RESERVATION_MODE` in the benchmark CLI so `quick-only`, `draft-only`, and `mixed` reservation traffic can be measured separately
- `SEKIBAN_BENCHMARK_SKIP_USER_RESERVATION_RULES=true` in the Native strict benchmark profile so the draft path does the same work as the C# WASM sample during runtime-comparison runs

`50K` reservation split evidence:

| Workload | Native strict ops/sec (old) | Native strict ops/sec (aligned) | C# WASM strict ops/sec | Reading |
|---|---:|---:|---:|---|
| `quick-only` | `596.0` | `596.0` | `411.0` | Native was already faster on the shared quick path |
| `draft-only` | `237.1` | `1590.3` | `563.2` | the old Native row was dominated by Native-only draft governance work |
| `mixed` | `423.9` | `939.7` | `571.3` | once draft work is aligned, Native is faster overall |

Latest comparable strict `300K` results:

| Runtime | Status | Weather events/sec | Reservation events/sec | Reservation ops/sec | Query ops/sec | Total wall-clock | Peak RSS | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| C# Native (`tagstategrain-memory`, latest package, benchmark-aligned draft path) | `completed` | `2029.5` | `2424.7` | `932.6` | `1796.5` | `139.0 s` | `~3615.6 MB` | `0` |
| C# WASM (`tagstategrain-memory`, fixed host) | `completed` | `1455.1` | `1529.6` | `588.3` | `917.2` | `203.4 s` | `~3552.4 MB` | `0` |
| Rust WASM (`tagstategrain-memory`, rebuilt sample module) | `completed` | `459.6` | `523.0` | `201.1` | `2128.7` | `622.1 s` | `~3060.4 MB` | `0` |
| MoonBit WASM (`tagstategrain-memory`) | `completed` | `515.0` | `163.0` | `63.0` | `166.0` | `1089.5 s` | `~2320.5 MB` | `0` |
| Go WASM (`tagstategrain-memory`) | `completed` | `510.0` | `190.0` | `73.0` | `816.0` | `986.6 s` | `~2980.4 MB` | `0` |
| TypeScript WASM (`tagstategrain-memory`) | `completed` | `475.0` | `196.0` | `75.0` | `418.0` | `992.5 s` | `~3999.7 MB` | `0` |

Comparable strict `50K` results:

| Runtime | Status | Weather events/sec | Reservation events/sec | Reservation ops/sec | Query ops/sec | Total wall-clock | Peak RSS | Errors |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| C# Native (`tagstategrain-memory`, latest package, benchmark-aligned draft path) | `completed` | `1858.6` | `2443.3` | `939.7` | `1874.8` | `24.8 s` | `~887.7 MB` | `0` |
| C# WASM (`tagstategrain-memory`, fixed host) | `completed` | `1302.2` | `1485.3` | `571.3` | `1012.4` | `37.3 s` | `~1775.4 MB` | `0` |
| Rust WASM (`tagstategrain-memory`, rebuilt sample module) | `completed` | `1030.4` | `1473.6` | `566.8` | `2469.3` | `43.3 s` | `~932.2 MB` | `0` |
| MoonBit WASM (`tagstategrain-memory`, aligned) | `completed` | `990.3` | `818.5` | `314.4` | `149.6` | `57.1 s` | `~745.7 MB` | `0` |
| Go WASM (`tagstategrain-memory`, aligned) | `completed` | `994.4` | `952.0` | `366.3` | `2106.9` | `51.9 s` | `~784.5 MB` | `0` |
| TypeScript WASM (`tagstategrain-memory`, aligned) | `completed` | `926.2` | `881.4` | `338.9` | `323.4` | `56.5 s` | `~732.6 MB` | `0` |

Latency summary:

| Runtime | Weather p50 / p95 | Reservation p50 / p95 | Query p50 / p95 |
|---|---|---|---|
| C# Native (`tagstategrain-memory`, latest package, benchmark-aligned draft path) | `3.8 / 5.3 ms` | `8.0 / 12.4 ms` | `0.2 / 0.4 ms` |
| C# WASM (`tagstategrain-memory`, fixed host) | `5.0 / 8.6 ms` | `12.3 / 18.8 ms` | `0.6 / 1.4 ms` |
| Rust WASM (`tagstategrain-memory`, rebuilt sample module) | `17.8 / 28.8 ms` | `34.8 / 65.7 ms` | `0.2 / 0.6 ms` |
| MoonBit WASM (`tagstategrain-memory`) | `15.7 / 24.3 ms` | `128.6 / 200.1 ms` | `5.7 / 17.9 ms` |
| Go WASM (`tagstategrain-memory`) | `15.9 / 26.0 ms` | `104.4 / 177.7 ms` | `1.0 / 2.4 ms` |
| TypeScript WASM (`tagstategrain-memory`) | `16.3 / 30.0 ms` | `102.0 / 170.0 ms` | `1.5 / 4.9 ms` |

Takeaways from the strict profile:

- All six implemented runtimes now complete the strict `300K` profile with `0` errors.
- Once the reservation workload is aligned, Native C# is faster than C# WASM at `10K`, `50K`, and `300K` for both reservation commands and queries.
- The other WASM runtimes were already on the lighter draft path, so the new 50K reruns mainly confirm their placement under the same comparable workload definition.
- After the projector-version fix, C# WASM no longer shows the pathological reservation collapse from the earlier strict run. It now completes `300K` with `588.3 reservation ops/sec` and stays under the `4 GB` guardrail at `~3552.4 MB`.
- The old strict conclusion "C# WASM is faster than Native at scale" was a benchmark artifact caused by Native-only draft governance work on the template path, not by WASM runtime superiority.
- With the draft path aligned, Native's `300K` reservation phase stays high throughout the run (`2567 eps` first sample -> `2304 eps` last sample) instead of collapsing.
- The remaining C# WASM penalty is now in the expected range for an out-of-process host: `36.9%` lower reservation ops/sec and `48.9%` lower query throughput than aligned Native at `300K`, while peak RSS stays in the same class (`~3.62 GB` vs `~3.55 GB`).
- Among the non-C# WASM runtimes, Rust now has the strongest strict reservation command path and is still the clear query-throughput leader.
- C# WASM and TypeScript WASM both stay under the original `4 GB` guardrail, but TypeScript remains extremely tight at `~3999.7 MB`.
- MoonBit WASM still has the lowest strict-profile memory usage at `~2.32 GB`; the rebuilt Rust sample trades memory for materially better command throughput and now peaks around `~3.06 GB`.
- The biggest remaining Native vs C# WASM difference is now query throughput and the extra remote-hop overhead, not a broken tag-state cache or a benchmark mismatch.

### C# WASM vs Native C# Strict Investigation

After aligning the Native draft path, the scale comparison is consistent:

| Scale | Native reservation ops/sec | C# WASM reservation ops/sec | Gap |
|---|---:|---:|---:|
| `10K` | `873.4` | `411.3` | C# WASM is `52.9%` slower |
| `50K` | `939.7` | `571.3` | C# WASM is `39.2%` slower |
| `300K` | `932.6` | `588.3` | C# WASM is `36.9%` slower |

Interpretation:

- Native is faster than C# WASM at every measured scale once both runtimes execute comparable reservation work.
- The remaining gap is no longer "multiple times slower" but a several-tens-of-percent penalty, which is much closer to the original expectation for a WASM host with an extra remote hop.
- Native no longer shows the severe reservation decay that the earlier mixed strict table suggested; that apparent collapse was mostly the cost of the Native-only draft governance path.
- The architectural caveat remains: C# WASM still pays `benchmark -> ClientApi -> WasmServer -> TagStateGrain`, while Native executes in-process.
- Even with that caveat, the strict 2026-04-09 reruns show that the dominant earlier regressions were the broken WASM tag-state cache and the Native/C# WASM draft-path mismatch, not unavoidable WASM overhead.

The saved 2026-04-08 HTTP-hop analysis is still useful as historical evidence for the pre-fix behavior, but it no longer describes the current strict C# WASM runtime.

### Strict 50K Validation

The `50,000` event reruns were used to verify the aligned comparison before the full `300K` pass and to fill in a comparable mid-scale table for every implemented runtime.

### Strict Profile Notes

The first strict C# WASM query run from 2026-04-08 was invalid because the checked-in `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/modules/sekiban-dcb-decider.wasm` was stale relative to the source tree. That stale module could not instantiate `RoomListProjection`, which caused `create_instance failed with code -1` during query catch-up.

This was fixed by:

- rebuilding the sample C# WASM module with `build/scripts/build-sample-csharp-wasm.sh`
- copying the fresh output back to `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/modules/sekiban-dcb-decider.wasm`
- making `scripts/run-benchmark-runtime.sh` auto-rebuild the sample module only when real source files (not `obj/` or `bin/` restore noise) are newer than the checked-in module

The 2026-04-09 C# WASM reruns additionally fixed the manifest/projector-version mismatch described above, which is why the latest strict rows differ so sharply from the earlier 2026-04-08 C# WASM line.

The original strict Rust rows from 2026-04-08 were also invalid for runtime-comparison purposes after the quick-reservation alignment work:

- the Rust source tree had been updated with `CreateQuickReservation`, `RoomReservationTag`, and `RoomReservationsProjector`
- but `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/modules/sekiban-dcb-decider-rust.wasm` had not been rebuilt
- the benchmark therefore exercised a stale module that could not instantiate `RoomReservationsProjector`, which surfaced as `create_instance failed with code -1` / tag-state `500` failures during reservation traffic

This was fixed by:

- rebuilding the sample Rust WASM module from `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/Cargo.toml`
- copying the fresh output back to `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/modules/sekiban-dcb-decider-rust.wasm`
- making `scripts/run-benchmark-runtime.sh` auto-rebuild the sample Rust module when Rust source or Cargo manifests are newer than the checked-in module

Corrected Rust validation after the rebuild:

- `50K quick-only`: `490.2 reservation ops/sec`, `1470.5 reservation events/sec`, `2491.3 query ops/sec`, `0` errors
- `50K mixed strict`: `566.8 reservation ops/sec`, `1473.6 reservation events/sec`, `2469.3 query ops/sec`, `0` errors
- `300K mixed strict`: `201.1 reservation ops/sec`, `523.0 reservation events/sec`, `2128.7 query ops/sec`, `0` errors

The strict TypeScript rerun initially failed for a different reproducibility reason: `ts-clientapi` had no local `node_modules`, so `npx tsx` could not resolve `@hono/node-server`. This was fixed by making `scripts/run-benchmark-runtime.sh` auto-run `npm install --no-audit --no-fund` for `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/ts-clientapi` when those dependencies are missing.

Result files:

- `benchmarks/results/native-10k-strict-aligned.json`
- `benchmarks/results/native-50k-quickonly.json`
- `benchmarks/results/native-50k-draftonly.json`
- `benchmarks/results/native-50k-draftonly-aligned.json`
- `benchmarks/results/native-50k-strict-aligned.json`
- `benchmarks/results/native-300k-strict-aligned.json`
- `benchmarks/results/cs-wasm-10k-postfix.json`
- `benchmarks/results/cs-wasm-50k-quickonly.json`
- `benchmarks/results/cs-wasm-50k-draftonly.json`
- `benchmarks/results/cs-wasm-50k-postfix.json`
- `benchmarks/results/cs-wasm-300k-postfix.json`
- `benchmarks/results/cs-wasm-300k-tagstategrain-memory-20260408-http-analysis.json`
- `benchmarks/results/rs-wasm-50k-quickonly-fixed2.json`
- `benchmarks/results/rs-wasm-50k-strict-fixed.json`
- `benchmarks/results/rs-wasm-300k-strict-fixed.json`
- `benchmarks/results/mb-wasm-50k-strict-aligned.json`
- `benchmarks/results/mb-wasm-300k-tagstategrain-memory-20260408.json`
- `benchmarks/results/go-wasm-50k-strict-aligned.json`
- `benchmarks/results/go-wasm-300k-tagstategrain-memory-20260408.json`
- `benchmarks/results/ts-wasm-50k-strict-aligned.json`
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
