# internalUsages

Internal usage examples for SekibanWasmRuntime. Two independent configurations demonstrate WASM-based projection using Sekiban DCB.

## Architecture

Each configuration (CS and Rust) uses a three-service topology:

```
[Web (Blazor)] → [ClientApi (HTTP adapter)] → [WasmServer (Orleans + WASM runtime)]
```

- **WasmServer**: Hosts Orleans silo, PostgreSQL EventStore, Wasmtime-based WASM projection runtime, command execution endpoints (`/api/weatherforecast/*`), and the `/v1/instances/*` projection instance HTTP API.
- **ClientApi**: A thin HTTP adapter that forwards `/api/weatherforecast/*` requests to WasmServer. CS uses ASP.NET, Rust uses `axum`.
- **Web**: Blazor Server frontend that consumes the ClientApi.

## Directory Structure

```
src/internalUsages/
├── cs/                    # C# WASM implementation
│   ├── SekibanWasm.Cs.AppHost/        # Aspire orchestrator
│   ├── SekibanWasm.Cs.WasmServer/     # WasmServer (Orleans + WASM projection + commands)
│   ├── SekibanWasm.Cs.ClientApi/      # ClientApi (HTTP adapter)
│   ├── SekibanWasm.Cs.Domain/         # Domain model (events, projectors, commands)
│   ├── SekibanWasm.Cs.ServiceDefaults/ # Aspire service defaults
│   ├── SekibanWasm.Cs.Tests/          # Unit tests
│   ├── SekibanWasm.Cs.Wasm/           # C# WASM module source
│   ├── SekibanWasm.Cs.Web/            # Blazor frontend
│   └── modules/                       # Built WASM binary (csharp-weather.wasm)
└── rust/                  # Rust WASM implementation
    ├── SekibanWasm.Rust.AppHost/      # Aspire orchestrator
    ├── SekibanWasm.Rust.WasmServer/   # WasmServer (Orleans + WASM projection + commands)
    ├── SekibanWasm.Rust.ClientApi/    # ClientApi (Rust/axum HTTP adapter)
    ├── SekibanWasm.Rust.ServiceDefaults/ # Aspire service defaults
    ├── SekibanWasm.Rust.Web/          # Blazor frontend
    └── modules/                       # Built WASM binary (rust-weather.wasm)
```

## CS vs Rust: Key Differences

| Aspect | CS | Rust |
|--------|----|----|
| WASM Module Language | C# (compiled to wasi-wasm) | Rust (compiled to wasm32-wasip1) |
| Build Script | `build/scripts/build-csharp-wasm.sh` | `build/scripts/build-rust-wasm.sh` |
| Build Requirement | .NET 10.0 SDK + Docker (non-Linux) | Rust toolchain + `wasm32-wasip1` target |
| WASM Binary | `cs/modules/csharp-weather.wasm` | `rust/modules/rust-weather.wasm` |
| Domain Layer | C# records with Sekiban DCB types | Shared bridge types in `src/lib/SekibanWasm.Rust.ServerBridge` |
| Projection Host | WasmServer with WasmProjectionRuntime | WasmServer with WasmProjectionRuntime |

Both configurations share the same architecture: Aspire AppHost orchestrates PostgreSQL, Azure Storage (emulated), Orleans, and the WasmServer + ClientApi + Web frontend.

## Comparison with DcbOrleans.Web (Sekiban Reference)

The reference implementation lives at `submodules/Sekiban/dcb/internalUsages/DcbOrleans.Web`.

### Common Points

| Aspect | Both |
|--------|------|
| Framework | ASP.NET + Blazor Server (Interactive SSR) |
| Aspire Integration | `AddServiceDefaults()`, service discovery |
| UI Pattern | Razor Components with `HttpClient`-based API clients |
| Routing | `MapRazorComponents<App>().AddInteractiveServerRenderMode()` |
| Static Assets | `MapStaticAssets()` |

### Differences

| Aspect | DcbOrleans.Web (Reference) | CS/Rust Web (This Repo) |
|--------|---------------------------|-------------------------|
| Domain Scope | Weather, Student, ClassRoom, Enrollment (4 domains) | Weather only (1 domain) |
| API Clients | `WeatherApiClient`, `StudentApiClient`, `ClassRoomApiClient`, `EnrollmentApiClient` | `WeatherApiClient` only |
| Projection Runtime | Native (C# in-process projection) | WASM (projection runs inside Wasmtime host) |
| Service Topology | Single ApiService | WasmServer + ClientApi (split) |
| Orleans Config | Full production setup (Cosmos/Blob/Table storage, streaming, multiple grain providers) | Minimal Aspire-managed setup |
| AppHost Features | Benchmark project, CLI tools, PgAdmin, DbGate | WasmServer + ClientApi + Web |
| Health Endpoint | Custom `/health` endpoint in Web | Via `MapDefaultEndpoints()` |
| CORS | Development CORS policy in ApiService | Not configured |
| OpenAPI | Scalar API reference UI | `MapOpenApi()` only |

### Architecture Difference: Native vs WASM Projection

The core architectural difference is how projections execute:

- **DcbOrleans (Reference)**: Projections run natively in the C# process via `AddSekibanDcbNativeRuntime()`. The Orleans grains directly invoke C# projector classes.
- **CS/Rust (This Repo)**: Projections run inside a Wasmtime sandbox via `WasmProjectionRuntime`. A `WasmProjectorRegistry` maps projector names to `.wasm` module files. This enables language-agnostic projection logic (C# or Rust compiled to WASM).

## Running

### Prerequisites

- .NET 10.0 SDK
- Docker Desktop (for Aspire emulators and C# WASM build on macOS)
- Rust toolchain with `wasm32-wasip1` target (for Rust WASM build)

### CS System

```bash
# Build C# WASM module
./build/scripts/build-csharp-wasm.sh

# Start the system
dotnet run --project src/internalUsages/cs/SekibanWasm.Cs.AppHost
```

### Rust System

```bash
# Build Rust WASM module
./build/scripts/build-rust-wasm.sh

# Start the system
dotnet run --project src/internalUsages/rust/SekibanWasm.Rust.AppHost
```

### Tests

```bash
dotnet test src/SekibanWasmRuntime.ci.slnx
```
