# Event Sourcing Benchmark Results

Comparison of Sekiban event sourcing performance across three runtime modes.

## Runtime Modes

| Mode | Description | Projection Runtime |
|------|-------------|-------------------|
| **C# Native** | Sekiban Orleans template, all projections in-process | Native .NET |
| **C# WASM** | Projections run in C# WASM module via Wasmtime | WASI Preview 1 (C#) |
| **Rust WASM** | Projections run in Rust WASM module via Wasmtime | WASI Preview 1 (Rust) |

## Test Environment

- **Machine**: Local development (Apple Silicon)
- **Database**: PostgreSQL (via Aspire emulator)
- **Orleans**: In-memory clustering (development mode)
- **Benchmark CLI**: `benchmarks/Sekiban.Benchmark.Cli`

## Benchmark Structure

### Phase 1: Setup
Create 20 meeting rooms. Validates basic connectivity.

### Phase 2: Weather Bulk (Pure Throughput)
Create weather forecasts in parallel (concurrency=8). Each call produces 1 event. No tag contention - UUID-based aggregates. Best measure of raw event write throughput.

### Phase 3: Reservation Lifecycle (Complex Domain)
QuickReservation workflow: creates draft + hold + confirm in a single call (3 events). Uses unique organizerId per request to minimize tag contention. Concurrency=2 to reduce optimistic concurrency conflicts.

### Phase 4/5: Query Performance
Sequential queries against the accumulated dataset: room list, reservation list (paginated), reservations by room, weather count.

## Results: 5,000 Event Smoke Test

| Metric | C# WASM | Rust WASM |
|--------|---------|-----------|
| **Weather Bulk** | | |
| Events/sec | 1,153 | 948 |
| p50 latency | 6.5ms | 8.0ms |
| p95 latency | 8.4ms | 10.3ms |
| p99 latency | 14.9ms | 15.4ms |
| Errors | 0 | 0 |
| **Reservation Lifecycle** | | |
| Events/sec | 308 | 323 |
| p50 latency | 10.6ms | 17.6ms |
| p95 latency | 16.3ms | 21.6ms |
| Errors | 351 | 0 |
| **Query Performance** | | |
| Queries/sec | 263 | 186 |
| p50 latency | 2.8ms | 2.3ms |
| p95 latency | 4.8ms | 12.3ms |
| **Total** | | |
| Events created | 5,026 | 5,026 |
| Wall-clock time | 10.8s | 11.3s |

## Results: 300,000 Event Test (CS WASM Partial)

Weather Bulk phase completed; Reservation phase was interrupted at ~70k events due to tag contention degradation.

| Phase | Events | Duration | Events/sec | p50 | p95 | p99 |
|-------|--------|----------|-----------|-----|-----|-----|
| Weather Bulk | 180,000 | 115.1s | 1,563 | 4.8ms | 6.8ms | 11.3ms |
| Reservation (partial) | ~70,000 | ~875s | ~80 | - | - | - |

### Key Observation: Throughput Degradation at Scale
Weather Bulk throughput remains stable at ~1,560 events/sec even at 180k events. Reservation throughput degrades from ~160 events/sec (at 10k) to ~80 events/sec (at 70k) due to growing projection state and tag contention.

## Analysis

### Weather Bulk (Pure Write Throughput)
- **C# WASM is ~22% faster** than Rust WASM for simple event writes (1,153 vs 948 events/sec)
- This reflects the overhead of the Rust ClientApi HTTP proxy layer (Axum/Reqwest) vs C# ClientApi (ASP.NET Core HttpClient), not WASM execution itself
- Both achieve excellent throughput for event sourcing workloads

### Reservation Lifecycle (Complex Domain Logic)
- **Rust WASM had 0 errors** vs 351 errors for C# WASM in the 5k test
- Tag contention on UserMonthlyReservation is more gracefully handled in the Rust pipeline
- Throughput is comparable (308 vs 323 events/sec)

### Query Performance
- C# WASM has higher overall query throughput (263 vs 186 queries/sec)
- Rust WASM has lower p50 latency (2.3ms vs 2.8ms) but higher p95 (12.3ms vs 4.8ms)

### Scalability
- Weather forecast writes scale linearly - throughput is constant even at 180k events
- Reservation lifecycle throughput degrades as event count grows, due to projection replay overhead
- This is a characteristic of the event sourcing model, not specific to WASM vs Native

## How to Run

### Prerequisites
- .NET 10 SDK
- Docker (for PostgreSQL and Azure Storage emulators via Aspire)

### C# WASM
```bash
# Start infrastructure
cd src/samples/Sekiban.Dcb.Orleans.Decider.Wasm
aspire start --isolated
aspire wait clientapi

# Run benchmark
cd benchmarks/Sekiban.Benchmark.Cli
dotnet run -c Release -- \
  --base-url http://localhost:5198 \
  --mode-label cs-wasm \
  --total-events 5000 \
  --concurrency 8

# Stop
aspire stop
```

### Rust WASM
```bash
# Start infrastructure
cd src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs
aspire start --isolated
aspire wait clientapi

# Run benchmark
cd benchmarks/Sekiban.Benchmark.Cli
dotnet run -c Release -- \
  --base-url http://localhost:6198 \
  --mode-label rs-wasm \
  --total-events 5000 \
  --concurrency 8

# Stop
aspire stop
```

### C# Native (Sekiban Template)
```bash
# Start from Sekiban template directory
cd /path/to/Sekiban-dcb/templates/Sekiban.Dcb.Templates/content/Sekiban.Dcb.Orleans.Decider
aspire start --isolated
aspire wait apiservice

# Run benchmark
cd /path/to/SekibanWasmRuntime/benchmarks/Sekiban.Benchmark.Cli
dotnet run -c Release -- \
  --base-url http://localhost:5141 \
  --mode-label native \
  --total-events 5000 \
  --concurrency 8

# Stop
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

## Future Work

- Run C# Native benchmark for direct comparison
- Cloud deployment benchmarks (Azure Container Apps)
- Memory profiling per runtime mode
- Larger scale tests (500k+ events)
- Reservation concurrency tuning to reduce tag contention
