# SekibanWasmRuntime Preview Packages

SekibanWasmRuntime provides preview packages for running Sekiban DCB projection
logic through WebAssembly contracts.

These packages are published as `1.0.0-preview.*` while the public runtime
contract, package split, and Wasmtime host policy are finalized.

## Package Selection

Install `Sekiban.Dcb.WasmRuntime` when you need the shared runtime contracts,
projection abstractions, serialized command/query DTOs, and in-process client
abstractions.

Install `Sekiban.Dcb.WasmRuntime.Remote` when your application talks to a
remote serialized Sekiban DCB runtime over HTTP.

Install `Sekiban.Dcb.WasmRuntime.Wasmtime` when you host WASM projections
in-process with Wasmtime. This package is part of the initial preview matrix and
may carry preview Wasmtime dependency behavior while the host integration is
stabilized.

## License

SekibanWasmRuntime is licensed under Elastic License 2.0. You may use, modify,
redistribute, and self-host SekibanWasmRuntime, including for internal company
use. You may not provide SekibanWasmRuntime to third parties as a hosted service,
managed service, SaaS, or similar offering that gives users access to a
substantial set of its features, unless a separate commercial license has been
agreed with J-Tech Japan.

Sekiban itself remains available under Apache License 2.0. The license for this
repository does not change upstream Sekiban package or submodule terms.

## Repository

Source, issue tracking, and release notes are maintained at
https://github.com/J-Tech-Japan/SekibanWasmRuntime.
