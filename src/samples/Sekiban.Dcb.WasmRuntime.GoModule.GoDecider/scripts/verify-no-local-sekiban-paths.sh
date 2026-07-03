#!/usr/bin/env bash
# External-consumer dependency guard for the Go published-module sample.
# The committed go.mod must consume the Go SDK exactly as an external user
# would: the published module path only — no replace directives, no local
# Sekiban paths. (The repo-committed go.work is the explicit dev-time overlay
# for pre-publish builds; the published-module smoke runs with GOWORK=off.)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

SAMPLE_DIR="src/samples/Sekiban.Dcb.WasmRuntime.GoModule.GoDecider"
GO_MOD="$SAMPLE_DIR/go.mod"

if rg -n '^replace' "$GO_MOD"; then
  echo "forbidden replace directive found in committed go.mod" >&2
  exit 1
fi

if rg -n '\.\./|\./src/lib' "$GO_MOD"; then
  echo "forbidden local Sekiban path found in committed go.mod" >&2
  exit 1
fi

if ! rg -q 'github\.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go v' "$GO_MOD"; then
  echo "go.mod must require the published module github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go" >&2
  exit 1
fi

# The end-to-end smoke must target the public GHCR runtime image, not a locally
# built runtime, so the sample proves published artifacts only.
APPHOST_PROGRAM="$SAMPLE_DIR/AppHost/Program.cs"
if [[ ! -f "$APPHOST_PROGRAM" ]]; then
  echo "missing AppHost Program.cs for the public GHCR runtime orchestration" >&2
  exit 1
fi
if ! rg -q 'ghcr\.io/j-tech-japan/sekiban-wasm-runtime-host' "$APPHOST_PROGRAM"; then
  echo "AppHost must target the public GHCR runtime image ghcr.io/j-tech-japan/sekiban-wasm-runtime-host" >&2
  exit 1
fi

go -C "$SAMPLE_DIR" build ./...
go -C "$SAMPLE_DIR" vet ./...

echo "Go published-module sample dependency guard passed"
