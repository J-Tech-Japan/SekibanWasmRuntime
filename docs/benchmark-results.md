# Event Sourcing Benchmark Results

Comparison of Sekiban event sourcing performance across three runtime modes with 300,000 events.

## Runtime Modes

| Mode | Description | Projection Runtime | ClientApi |
|------|-------------|-------------------|-----------|
| **C# Native** | Sekiban Orleans template, all projections in-process | Native .NET | Direct ApiService |
| **C# WASM** | Projections run in C# WASM module via Wasmtime | WASI Preview 1 (C#) | ASP.NET Core proxy |
| **Rust WASM** | Projections run in Rust WASM module via Wasmtime | WASI Preview 1 (Rust) | Rust Axum proxy |

## Test Environment

- **Machine**: Apple Silicon (local development)
- **Database**: PostgreSQL (via Aspire container emulator)
- **Orleans**: In-memory clustering (development mode)
- **Benchmark CLI**: `benchmarks/Sekiban.Benchmark.Cli`
- **Concurrency**: 8 parallel HTTP clients
- **Date**: 2026-03-31 (post-merge re-run)

## 300,000 Event Benchmark Results

### Weather Bulk Write (180,000 events, concurrency=8)

Pure event write throughput with no tag contention (UUID-based aggregates).

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Events/sec** | **1,843** | 1,566 | 437 |
| **Duration** | **97.7s** | 114.9s | 411.8s |
| **p50 latency** | **4.1ms** | 4.7ms | 18.7ms |
| **p95 latency** | **5.8ms** | 7.4ms | 30.8ms |
| **p99 latency** | **10.1ms** | 12.7ms | 38.7ms |
| **Errors** | 0 | 0 | 0 |
| **Throughput stability** | Stable ~1,843/s | Stable ~1,566/s | Degrading 1,100→437/s |

### Reservation Lifecycle (120,000 event target, concurrency=8)

Complex domain workflow: QuickReservation (draft + hold + confirm = 3 events/call) with unique user IDs per request to minimize tag contention.

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Events created** | 6,854* | 120,024 | 120,023 |
| **Events/sec** | 12* | 128 | 130 |
| **p50 latency** | 1.5ms* | 142.2ms | 154.4ms |
| **p95 latency** | 352.0ms* | 317.5ms | 253.4ms |
| **Errors** | 73,156* | 1,559 | **0** |

*Native Reservation performance is not comparable: JWT authentication means all requests run as the same user, causing severe UserMonthlyReservation tag contention. WASM samples use per-request unique user IDs via X-Debug-User-Id header.

### Query Performance (50 iterations, after all writes)

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Queries/sec** | **1,338** | 0.2* | 4 |
| **p50 latency** | **0.2ms** | 24.0ms | 18.7ms |
| **p95 latency** | **0.5ms** | 30,003ms* | 635.3ms |
| **Errors** | 0 | 35* | 0 |

*CS WASM query performance degraded significantly after 300k events, with some queries timing out at 30s. This reflects the cost of WASM projection replay at scale.

### Overall Summary

| Metric | C# Native | C# WASM | Rust WASM |
|--------|-----------|---------|-----------|
| **Total events** | 186,874 | 300,044 | 300,043 |
| **Total wall-clock** | 657.6s | 2,163.2s | 1,389.5s |
| **Total errors** | 73,156 | 1,594 | **0** |

## Analysis

### 1. Weather Bulk (Pure Write Throughput)

This is the fairest comparison since it measures pure event write + projection throughput without tag contention.

- **C# Native is 18% faster than C# WASM** (1,843 vs 1,566 events/sec) - the WASM sandboxing overhead is modest
- **C# WASM is 3.6x faster than Rust WASM** (1,566 vs 437 events/sec) - Rust WASM throughput still degrades as event count grows, though optimizations improved it by 64% from original 267
- **Native and CS WASM maintain stable throughput** even at 180k events, while Rust WASM drops from 960 to 267 events/sec

### 2. Scalability Characteristics

**C# Native**: Flat throughput curve. Events/sec remains constant from 0 to 180k events (~1,843/s throughout).

**C# WASM**: Nearly flat. Slight dip around 35-50k events then recovers. The WASM layer adds consistent overhead but doesn't degrade at scale.

**Rust WASM**: Still degrading after all optimizations. Starts at 1,100 events/sec but falls to 437 events/sec at 180k events (improved from original 960→267). Multiple optimizations applied: executor tag-state caching (+49%), HashMap-based multi-projector state (+10%), registry caching, query clone elimination. Remaining degradation is WasmServer-side event replay cost.

### 3. Reservation Lifecycle

The QuickReservation workflow is a realistic complex operation (3+ events, multiple tag projections, room state lookup). CS WASM achieved 128 events/sec and Rust WASM achieved 100 events/sec with minimal errors when using unique user IDs.

### 4. Query Performance at Scale

- **Native queries are sub-millisecond** (0.2ms p50) because projections are in-process and cached in Orleans grains
- **WASM queries degrade with event count** because each query may trigger projection replay through the WASM module
- Rust WASM queries (18.5ms p50) perform better than CS WASM (24.0ms p50) at 300k scale

### 5. Error Characteristics

- **Rust WASM: 0 errors** in the entire 300k run - most robust
- **CS WASM: 1,559 errors** - occasional tag contention even with unique users
- **Native: 73,156 errors** - JWT auth means single-user, causing massive tag contention (not a fair comparison for reservations)

## WASM Overhead Cost

Based on Weather Bulk (most comparable metric):

| Comparison | Overhead |
|------------|----------|
| C# WASM vs Native | **+15%** latency, **-15%** throughput |
| Rust WASM vs Native | **+356%** latency at 180k events |
| Rust WASM vs C# WASM | **+298%** latency at 180k events |

The C# WASM overhead is relatively modest (~15% throughput reduction), making it a viable option for production where WASM sandboxing is desired. The Rust WASM runtime shows significant scalability issues that need investigation.

## How to Run

### Prerequisites
- .NET 10 SDK
- Docker (for PostgreSQL and Azure Storage emulators via Aspire)
- Rust toolchain (for Rust WASM sample)

### C# WASM
```bash
cd src/samples/Sekiban.Dcb.Orleans.Decider.Wasm
aspire start --isolated && aspire wait clientapi

cd benchmarks/Sekiban.Benchmark.Cli
dotnet run -c Release -- \
  --base-url http://localhost:5198 \
  --mode-label cs-wasm \
  --total-events 300000 \
  --concurrency 8 \
  --output benchmarks/results/cs-wasm-300k.json

aspire stop
```

### Rust WASM
```bash
cd src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs
aspire start --isolated && aspire wait clientapi

cd benchmarks/Sekiban.Benchmark.Cli
dotnet run -c Release -- \
  --base-url http://localhost:6198 \
  --mode-label rs-wasm \
  --total-events 300000 \
  --concurrency 8 \
  --output benchmarks/results/rs-wasm-300k.json

aspire stop
```

### C# Native (from Sekiban template)
```bash
cd /path/to/Sekiban-dcb/templates/Sekiban.Dcb.Templates/content/Sekiban.Dcb.Orleans.Decider
aspire start --isolated && aspire wait apiservice
# Note the dynamic port from 'aspire describe'

cd benchmarks/Sekiban.Benchmark.Cli
dotnet run -c Release -- \
  --base-url http://localhost:<PORT> \
  --mode-label native \
  --total-events 300000 \
  --concurrency 8 \
  --output benchmarks/results/native-300k.json

aspire stop
```

### CLI Options
```
--base-url <url>        Target API base URL (required)
--mode-label <label>    Runtime mode label: native, cs-wasm, rs-wasm (required)
--total-events <n>      Target event count (default: 300000)
--concurrency <n>       Parallel HTTP clients (default: 8)
--output <path>         Output JSON file path
--skip-setup            Skip room creation phase
```

## Tag Contention Notes

The `UserMonthlyReservationTag` uses a `{UserId}_{yyyy-MM}` key format. When multiple concurrent requests create reservations for the same user in the same month, optimistic concurrency conflicts occur.

**Mitigation applied in benchmark**: Each request uses a unique `X-Debug-User-Id` header (WASM samples) so each reservation appears as a different user, distributing load across different tag partitions.

**Limitation for Native mode**: JWT authentication ties all requests to a single user identity, making it impossible to distribute tag load. This means Native Reservation benchmarks reflect worst-case single-user contention rather than realistic multi-user throughput.

## Performance Tuning Applied

### 1. Rust Executor Tag State Caching (sekiban-executor)

**Problem**: `HttpCommandContext.get_tag_state()` made redundant HTTP calls to WasmServer.

**Fix**: Added `tag_state_cache` HashMap to `HttpCommandContext`. First call fetches from WasmServer and caches; subsequent calls return cached result.

**Impact**: Weather Bulk improved from 267 → 399 events/sec (+49%).

### 2. Multi-Projector Vec → HashMap Conversion (domain code)

**Problem**: All multi-projector states used `Vec<T>` with O(n) linear search for updates/lookups.

**Fix**: Converted to `HashMap<Uuid, T>` for O(1) lookups. Affected: WeatherForecastList, StudentList, ClassRoomList, UserDirectory, UserAccess, RoomList, ReservationList, ApprovalInbox.

**Impact**: Weather Bulk improved from 399 → 437 events/sec (+10%). Query execution also benefits from avoiding `.cloned().collect()`.

### 3. Registry Caching (sekiban-wasm library)

**Problem**: `create_instance_by_name()` rebuilt the projector factory registry on every WASM instance creation.

**Fix**: Cache registry in thread-local storage, build once per domain type.

### 4. Query Clone Elimination (domain code)

**Problem**: Query execution cloned entire state vectors before sorting/filtering.

**Fix**: Use references (`state.items.values().collect()`) instead of `.cloned().collect()`.

### 5. WasmServer DirectTagStateCache (WasmRuntime.Host)

**Problem**: Every tag-state request replayed ALL historical events from scratch.

**Fix**: Added `DirectTagStateCache` that stores tag state snapshots in memory. Subsequent requests restore from snapshot and only replay events after the snapshot's sortable unique ID.

### Cumulative Improvement

| Metric | Original | After All Optimizations | Improvement |
|--------|----------|------------------------|-------------|
| Weather Bulk events/sec | 267 | 437 | **+64%** |
| Weather Duration | 674s | 412s | **-39%** |
| Total wall-clock | 1,929s | 1,390s | **-28%** |
| Total errors | 0 | 0 | - |

**Remaining gap**: CS WASM (1,566 events/sec) is still 3.6x faster. The remaining difference is due to WasmServer-side full event replay for each unique tag (not yet cached in this test run) and Rust WASM module's serialization overhead vs C# WASM.

## Future Work

- Cloud deployment benchmarks (Azure Container Apps)
- Memory profiling per runtime mode
- Investigate WasmServer-side projection caching for Rust WASM (Orleans grain snapshot optimization)
- 500k+ event tests
- Multi-user JWT authentication for fairer Native Reservation comparison
