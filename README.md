# SekibanWasmRuntime

Runtime for WASM on Sekiban. Enables language-agnostic event projections by running projector logic inside a Wasmtime sandbox, supporting both C# and Rust WASM modules.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://docs.docker.com/get-docker/) (for Aspire emulators; also required for C# WASM build on macOS)
- [Rust toolchain](https://rustup.rs/) with `wasm32-wasip1` target (for Rust WASM build)

Install the Rust WASM target:

```bash
rustup target add wasm32-wasip1
```

## Project Structure

```
src/
├── lib/                               # Core WASM runtime libraries
│   ├── Sekiban.Dcb.WasmRuntime/       # Runtime abstractions
│   ├── Sekiban.Dcb.WasmRuntime.Remote/    # Remote runtime support
│   └── Sekiban.Dcb.WasmRuntime.Wasmtime/  # Wasmtime host integration
├── internalUsages/
│   ├── cs/                            # C# WASM example (Aspire + Blazor)
│   └── rust/                          # Rust WASM example (Aspire + Blazor)
└── wasm-projectors/
    └── rust/                          # Rust WASM projector source
```

See [src/internalUsages/README.md](src/internalUsages/README.md) for detailed architecture and comparison with the Sekiban reference implementation.

## Quick Start

### C# WASM Example

```bash
# 1. Build the C# WASM module
./build/scripts/build-csharp-wasm.sh

# 2. Start the Aspire-orchestrated system
dotnet run --project src/internalUsages/cs/SekibanWasm.Cs.AppHost
```

### Rust WASM Example

```bash
# 1. Build the Rust WASM module
./build/scripts/build-rust-wasm.sh

# 2. Start the Aspire-orchestrated system
dotnet run --project src/internalUsages/rust/SekibanWasm.Rust.AppHost
```

## Testing

```bash
# Run all tests
dotnet test src/SekibanWasmRuntime.ci.slnx

# Run E2E smoke tests (builds WASM modules + runs tests)
./build/scripts/run-e2e.sh
```

## Submodules

This repository uses git submodules. Initialize them after cloning:

```bash
git submodule update --init --recursive
```
