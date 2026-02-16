# Implementation Checklist (SekibanWasmRuntime)

## Baseline
- [ ] Submodule Sekiban points to a commit containing MultiProjection primitive abstraction.
- [ ] Baseline SHAs are recorded in PR body.

## Code
- [ ] WASM primitive class for MultiProjection is implemented.
- [ ] Runtime DI registration supports primitive replacement.
- [ ] Runtime selection behavior is explicit and fail-fast.

## Tests
- [ ] Added parity tests for init/restore/catch-up/version mismatch.
- [ ] Existing internal usage tests pass.

## CI Commands
- [ ] `dotnet build src/SekibanWasmRuntime.ci.slnx -c Release`
- [ ] `dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build`
- [ ] `./build/scripts/build-csharp-wasm.sh`
- [ ] `./build/scripts/build-rust-wasm.sh`
