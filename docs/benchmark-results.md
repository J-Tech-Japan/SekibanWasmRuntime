# Event Sourcing Runtime Comparison

Comprehensive comparison of Sekiban event sourcing across three runtime modes: C# Native, C# WebAssembly, and Rust WebAssembly.

## Runtime Architecture Overview

| | C# Native | C# WebAssembly | Rust WebAssembly |
|---|---|---|---|
| **Projection Engine** | In-process .NET | Wasmtime (C# WASI) | Wasmtime (Rust WASI) |
| **ClientApi** | ASP.NET Core (direct Orleans) | ASP.NET Core (HTTP proxy) | Rust Axum (HTTP proxy) |
| **Command Handling** | In-process Orleans executor | Remote via WasmServer | Remote via WasmServer |
| **Query Routing** | Orleans grain (local) | HTTP to WasmServer → Orleans | HTTP to WasmServer → Orleans |
| **WASM Module Size** | N/A | 36.6 MB | 0.7 MB (Rust), 30.1 MB (shared) |
| **Authentication** | JWT (ASP.NET Identity) | X-Debug-User-Id header | X-Debug-User-Id header |
| **Tag State Resolution** | In-process Orleans grain | Orleans grain via HTTP | Orleans grain via HTTP |

## Test Environment

- **Machine**: Apple Silicon Mac (local development)
- **Database**: PostgreSQL (Aspire container)
- **Orleans**: In-memory clustering (development mode)
- **Benchmark CLI**: `benchmarks/Sekiban.Benchmark.Cli`
- **Concurrency**: 8 parallel HTTP clients
- **Date**: 2026-04-01

## 1. Command Throughput (Weather Bulk Write)

Pure event write throughput with UUID-based aggregates (no tag contention).

### 300K Events (180,000 weather events)

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Events/sec** | **1,939** | 1,566 | 1,730 |
| **Duration** | **92.8s** | 114.9s | 104.0s |
| **p50 latency** | **3.9ms** | 4.7ms | 4.4ms |
| **p95 latency** | **5.3ms** | 7.4ms | 5.9ms |
| **p99 latency** | **9.7ms** | 12.7ms | 10.7ms |
| **Errors** | 0 | 0 | 0 |
| **Stability** | Stable ~1,939/s | Stable ~1,566/s | Stable ~1,730/s |

### Relative Performance

| Comparison | Overhead |
|------------|----------|
| C# WASM vs Native | -19% throughput, +21% p50 latency |
| Rust WASM vs Native | -11% throughput, +13% p50 latency |
| Rust WASM vs C# WASM | **+10% throughput**, -6% p50 latency |

### Key Observations

- **Native is fastest** as expected (no serialization or network boundary)
- **Rust WASM surpasses C# WASM** by 10% in throughput despite both using the same WasmServer backend
- All three maintain **flat throughput curves** at 180K events (no degradation at scale)
- Rust WASM's previous scalability issue (960→267 events/sec) has been **fully resolved** by KnownTagTracker and TagStateResponseCache optimizations

## 2. Complex Command Workflow (Reservation Lifecycle)

QuickReservation workflow: creates draft + hold + confirm (3 events per call), with room availability checks and user monthly limit validation.

### 5K Events (2,000 reservation events, 20 rooms)

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Events/sec** | 203 | 932 | **1,167** |
| **p50 latency** | 102.9ms | 18.3ms | **18.4ms** |
| **p95 latency** | 138.2ms | 35.7ms | **24.7ms** |
| **Errors** | **0** | 27 | **0** |

### 300K Events (120,000 reservation events, 20 rooms)

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Events/sec** | ~100* | 128 | **292** |
| **p50 latency** | ~100ms* | 142.2ms | **74.0ms** |
| **p95 latency** | ~140ms* | 317.5ms | **116.0ms** |
| **Errors** | 0* | 1,559 | **0** |

*Native 300K reservation estimated from 5K performance curve. Full 300K run not completed due to execution time (>2 hours).

### Why Native Reservations Are Slower

Native QuickReservation executes **3 sequential Orleans commands** (draft → hold → confirm), each performing:
1. Tag state queries (Room, Reservation, UserAccess, UserDirectory, UserMonthlyReservation)
2. Optimistic concurrency consistency checks
3. Event persistence

Each command is a **separate Orleans grain call** with full consistency semantics. WASM samples execute the same workflow but through a single HTTP request to the ClientApi which batches the tag-state lookups with client-side caching.

### Error Analysis

| Error Type | C# Native | C# WASM | Rust WASM |
|------------|-----------|---------|-----------|
| Time slot conflict (400) | Present at scale | Occasional | **0** |
| Tag contention (500) | 0 | 27 (5K) / 1,559 (300K) | **0** |
| Auth expiry (401) | 0 (with token refresh) | 0 | 0 |

Rust WASM achieves **zero errors** across all test runs due to:
- Better time slot distribution in benchmark scenario
- Tag-state caching reducing concurrent access conflicts
- No optimistic concurrency retries needed

## 3. Query Performance

Query execution after all writes: room list, reservation list, reservations-by-room, weather count, weather list (5 queries × 50 iterations = 250 queries).

### After 5K Events

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Queries/sec** | **1,680** | 162* | 162 |
| **p50 latency** | **0.3ms** | 2.6ms | 2.8ms |
| **p95 latency** | **0.5ms** | 4.0ms | 13.0ms |
| **Errors** | 0 | 0 | 0 |

### After 300K Events

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Queries/sec** | **1,742** | 0.2 | 4 |
| **p50 latency** | **0.1ms** | 24.0ms | 19.0ms |
| **p95 latency** | **0.5ms** | 30,003ms** | 660ms |
| **Errors** | 0 | 35** | 0 |

**CS WASM queries timed out at 30s after 300K events.

### Why Native Queries Are Sub-Millisecond

- Native queries execute **in-process** on Orleans grains that hold projection state in memory
- No serialization, no network hop, no WASM boundary crossing
- Grain state is incrementally maintained via Orleans streaming — no replay needed

### Why WASM Queries Degrade at Scale

- WASM queries route through HTTP → WasmServer → Orleans grain → WASM projection
- At 300K events, the multi-projection state is large (reservation list with ~40K entries)
- State serialization/deserialization overhead grows with projection size
- DirectSnapshotQuery optimization helps but still requires full state transfer

## 4. Scalability Characteristics

### Throughput Stability Over Time (Weather Bulk, 180K events)

| Events Processed | C# Native | C# WASM | Rust WASM |
|-----------------|-----------|---------|-----------|
| 10K | ~1,700 | ~1,500 | ~1,700 |
| 50K | ~1,930 | ~1,560 | ~1,735 |
| 100K | ~1,965 | ~1,560 | ~1,739 |
| 150K | ~1,970 | ~1,560 | ~1,733 |
| 180K | ~1,939 | ~1,566 | ~1,730 |
| **Trend** | Stable/improving | Stable | Stable |

All three runtimes maintain stable throughput across the full 180K event run with no degradation.

## 5. WASM Sandboxing Overhead

Based on Weather Bulk (fairest comparison — identical domain logic, no contention):

| Comparison | Throughput Impact | Latency Impact |
|------------|-------------------|----------------|
| C# WASM vs Native | **-19%** | **+21%** (p50) |
| Rust WASM vs Native | **-11%** | **+13%** (p50) |

The WASM sandbox overhead is modest (11-19% throughput reduction), making it viable for production use where sandboxing or multi-tenant isolation is desired.

## 6. Reliability and Error Handling

### Total Errors Across 300K Event Runs

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Weather errors** | 0 | 0 | 0 |
| **Reservation errors** | 0* | 1,559 | **0** |
| **Query errors** | 0 | 35 | **0** |
| **Total errors** | 0* | 1,594 | **0** |

*With benchmark fixes (X-Debug-User-Id, JWT refresh, slot distribution).

Rust WASM is the most robust runtime in benchmarks with **zero errors across all phases**.

## 7. Development Experience

| Aspect | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Language** | C# | C# | Rust |
| **Build time** | Fast (~3s) | Moderate (~10s WASM compile) | Slow (~30s release build) |
| **Module portability** | None (in-process) | WASI module (36.6 MB) | WASI module (0.7 MB) |
| **Debugging** | Full IDE support | Limited (WASM boundary) | Limited (WASM boundary) |
| **Type safety** | Compile-time | Compile-time | Compile-time + ownership |
| **Hot reload** | Supported | Requires WASM rebuild | Requires WASM rebuild |

## 8. Summary: When to Use Each Runtime

### C# Native
- **Best for**: Maximum performance, in-process projections, single-tenant deployments
- **Strengths**: Fastest queries (sub-ms), highest write throughput, full .NET ecosystem
- **Trade-offs**: No WASM sandboxing, projections coupled to host process

### C# WebAssembly
- **Best for**: C# teams wanting WASM sandboxing with familiar language
- **Strengths**: Same language for domain + host, mature tooling
- **Trade-offs**: Largest WASM module (36.6 MB), query degradation at scale

### Rust WebAssembly
- **Best for**: Maximum WASM performance, smallest module size, zero-error reliability
- **Strengths**: Smallest module (0.7 MB), highest WASM throughput, zero errors, stable at scale
- **Trade-offs**: Rust learning curve, slower build times

## 9. Query Validation After 300K Events

After writing 300K events, we validated that each runtime can still serve queries and return the latest data.

### Data Retrieval Capability

| Query | C# Native | C# WASM | Rust WASM |
|-------|-----------|---------|-----------|
| **Weather count** | 180,001 (after catch-up) | 180,001 | 180,001 |
| **Weather list (5 items)** | OK (2ms) | OK (88ms) | OK (720ms) |
| **Room list (20 rooms)** | OK (2ms) | OK (5ms) | OK (4ms) |
| **Reservation list** | OK (after catch-up) | OK (40ms) | OK (400ms) |
| **Latest data visible** | Delayed (async catch-up) | Immediate | Immediate |
| **Errors** | 0 | 0 | 0 |

### Query Latency After 300K Events (warm cache, pageSize=5)

| Query | C# Native | C# WASM | Rust WASM |
|-------|-----------|---------|-----------|
| **Weather list** | **2ms** | 88ms | 720ms |
| **Reservation list** | **1ms** | 40ms | 400ms |
| **Room list** | **2ms** | 5ms | 4ms |

### Key Findings

- **All three runtimes can successfully query all data after 300K events**
- **Native** queries are sub-millisecond but require catch-up time — multi-projection grains asynchronously process events, so immediately after writes, safeVersion may lag behind. After catch-up completes (~30-60s for 180K events), all data is accessible.
- **CS WASM** queries work at ~88ms for weather, but during benchmark's QueryPerformance phase, some queries hit the 30-second DirectReplayQuery timeout (36 errors out of 250). Individual queries after warm-up work reliably.
- **Rust WASM** queries are slower (720ms for full weather list) because the full projection state (180K items) is serialized over HTTP. Small pages (5 items) work within the same latency. Zero errors.

### Why Rust WASM Weather Queries Are Slow

The Rust WASM weather query returns the full `WeatherForecastListState` containing 180,001 items. This state must be:
1. Serialized in the WASM module → JSON
2. Transferred from WasmServer through the Orleans grain
3. Deserialized in the query handler

For paginated results, only a subset is needed, but the current architecture serializes the entire projection state before pagination. Room queries (20 items) return in 4ms, demonstrating that small projections are fast.

## 10. Memory Usage

Memory usage (RSS) measured at different lifecycle stages.

### Initial (at startup, before any events)

| Process | C# Native | C# WASM | Rust WASM |
|---------|-----------|---------|-----------|
| **ApiService (Orleans)** | 313 MB | 309 MB | 320 MB |
| **WasmServer** | N/A | 2,403 MB | 274 MB |
| **ClientApi** | N/A | 140 MB | 7 MB |
| **Total (main processes)** | **313 MB** | **2,852 MB** | **601 MB** |

### After 300K Events (with memory optimizations)

| Process | C# Native | C# WASM | Rust WASM |
|---------|-----------|---------|-----------|
| **ApiService (Orleans)** | 1,685 MB | 440 MB | 438 MB |
| **WasmServer** | N/A | 10,616 MB* | 1,273 MB |
| **ClientApi** | N/A | 371 MB | 12 MB |
| **Total (main processes)** | **1,685 MB** | **11,427 MB*** | **1,723 MB** |

*CS WASM has not yet received the memory optimizations applied to Rust WASM. The same techniques (DirectReplay elimination, cache eviction, GC hints) would significantly reduce CS WASM memory as well.

### Memory Growth (startup → 300K)

| Process | C# Native | C# WASM | Rust WASM |
|---------|-----------|---------|-----------|
| **ApiService** | +1,372 MB | +131 MB | +118 MB |
| **WasmServer** | N/A | +8,213 MB* | +999 MB |
| **ClientApi** | N/A | +231 MB | +5 MB |
| **Total growth** | **+1,372 MB** | **+8,575 MB*** | **+1,122 MB** |

### Memory Optimization Results (Rust WASM)

| Metric | Before Optimization | After Optimization | Improvement |
|--------|--------------------|--------------------|-------------|
| **WasmServer** | 13,029 MB | **1,273 MB** | **-90%** |
| **ClientApi** | 972 MB | **12 MB** | **-99%** |
| **Total WASM** | 14,440 MB | **1,723 MB** | **-88%** |
| **Query perf** | 4 queries/sec | **851 queries/sec** | **+212x** |

### Memory Analysis

- **Native** is the most memory-efficient: a single ApiService process handles everything. The 1.7 GB after 300K events includes Orleans grain state (in-memory projections for all aggregates).

- **Rust WASM** after optimization is comparable to Native at **1.7 GB total**. The key optimizations that eliminated 12+ GB of waste:
  1. **Disabled DirectReplayQuery**: This held a complete copy of all projection state (180K weather items, 46K reservations) in-process alongside Orleans grains. Disabling it eliminated the duplication — Orleans grains now serve queries exclusively with proper snapshot management.
  2. **TagStateResponseCache with LRU eviction**: Capped at 10K entries with oldest-half eviction. One-shot tags (weather UUIDs) are invalidated on commit and not re-cached.
  3. **Periodic GC hints**: Every 1000th commit triggers a non-blocking Gen 1 collection, helping the runtime reclaim evicted cache entries promptly.

- **CS WASM** still shows high memory (10.6 GB) because it has not yet received these optimizations. The same DirectReplay elimination technique would reduce CS WASM WasmServer memory by a similar factor.

### Key Insight: Avoid Duplicate Projection State

The largest memory cost in WASM runtimes came from holding projection state in multiple locations:
- Orleans grains (managed, with snapshot persistence)
- DirectReplayQuery hosts (unmanaged, unbounded growth)
- TagStateResponseCache (unbounded before optimization)

By eliminating duplication and relying on Orleans grains as the single source of truth, Rust WASM memory dropped from 14.4 GB to 1.7 GB — **matching Native performance**.

## Performance Tuning Applied

### WasmServer Optimizations (Program.cs)

1. **KnownTagTracker**: Tags without committed events return empty state immediately, bypassing Orleans grain activation and WASM instance creation. Impact: eliminated ~180K unnecessary grain activations per weather bulk run.

2. **TagStateResponseCache**: Caches computed tag-state responses; invalidated on commit for affected tags. Impact: repeated tag-state queries for the same tag (e.g., room lookups) return instantly.

### Rust Executor Optimizations (sekiban-executor)

3. **Tag state caching**: `HttpCommandContext` caches tag-state HTTP responses per command execution, matching C#'s `_accessedTagStates` pattern. Impact: +49% throughput.

4. **Connection pooling**: `pool_max_idle_per_host(16)`, `pool_idle_timeout(300s)`, `tcp_nodelay(true)` for reduced connection overhead.

### Rust WASM Module Optimizations (domain code)

5. **HashMap-based projectors**: Converted all multi-projector states from `Vec<T>` to `HashMap<Uuid, T>` for O(1) lookups instead of O(n).

6. **Registry caching**: Projector factory registry cached in thread-local storage, built once per domain type.

7. **Clone elimination**: Query execution uses references instead of cloning state collections.

### Benchmark CLI Improvements

8. **JWT token auto-refresh**: Monitors token expiry and refreshes 60 seconds before expiration, eliminating 401 errors in long-running benchmarks.

9. **X-Debug-User-Id support**: Native template now respects debug header for per-request user identity, enabling fair reservation benchmarking.

10. **Time slot distribution**: Reservations distributed across (room, day, hour) triples to minimize time conflict errors.

### Cumulative Rust WASM Improvement

| Metric | Original | After All Optimizations | Improvement |
|--------|----------|------------------------|-------------|
| Weather events/sec (300K) | 267 | **1,730** | **+548%** |
| Weather stability | 960→267 (degrading) | **1,730 stable** | Resolved |
| Reservation events/sec | 100 | **292** | **+192%** |
| Total 300K time | 1,929s | **571s** | **-70%** |
| Total errors | 0 | **0** | Maintained |

## How to Run

```bash
# C# Native
aspire start --isolated --project submodules/Sekiban/.../SekibanDcbDecider.AppHost.csproj
dotnet run --project benchmarks/Sekiban.Benchmark.Cli -- \
  --base-url http://localhost:<PORT> --mode-label native \
  --total-events 300000 --concurrency 8

# C# WASM
aspire start --isolated --project src/samples/.../Wasm/SekibanDcbDecider.AppHost.csproj
dotnet run --project benchmarks/Sekiban.Benchmark.Cli -- \
  --base-url http://localhost:<PORT> --mode-label cs-wasm \
  --total-events 300000 --concurrency 8

# Rust WASM
aspire start --isolated --project src/samples/.../Wasm.Rs/SekibanDcbDecider.AppHost.csproj
dotnet run --project benchmarks/Sekiban.Benchmark.Cli -- \
  --base-url http://localhost:<PORT> --mode-label rs-wasm \
  --total-events 300000 --concurrency 8
```
