#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

echo "[run-e2e] Building WASM modules..."
"$ROOT/build/scripts/build-csharp-wasm.sh"
"$ROOT/build/scripts/build-rust-wasm.sh"

echo "[run-e2e] Running tests..."
dotnet test "$ROOT/src/SekibanWasmRuntime.slnx" -c Release

echo "[run-e2e] All tests passed."
