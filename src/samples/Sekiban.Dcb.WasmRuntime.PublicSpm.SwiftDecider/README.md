# Swift SPM External-Consumer Sample (SWR-G063)

This sample is the **Swift external-consumer proof** for SekibanWasmRuntime:
its committed `Package.swift` depends on the repository-root package
`https://github.com/J-Tech-Japan/SekibanWasmRuntime` at
**exact 1.0.0-preview.4** — no
path-based package references (guarded by
`scripts/verify-no-local-sekiban-paths.sh`) — imports only the fixed public
products `SekibanWasm` / `SekibanMv`, and proves the four consumer checks
against the **public GHCR runtime container**
(`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`):

1. **Command execution** — `WeatherForecastCreated` +
   `WeatherForecastLocationUpdated` commits through the serialized endpoint.
2. **Tag-state readback** — `tag-latest-sortable` reflects the committed tag.
3. **In-memory projection query** — `GetWeatherForecastListQuery` returns the
   forecast with the updated location.
4. **Materialized-view catch-up/read** — the `WeatherForecast` MV row (updated
   location) appears in `DcbMaterializedViewPostgres`.

The domain mirrors the Rust and Go published-artifact samples (same events,
same MV SQL, same manifest shape) so the evidence is comparable across SDK
languages.

## Layout

```text
Package.swift   Depends on the repository-root package at exact 1.0.0-preview.4
Sources/        WeatherForecast domain + C-ABI entry points (wasm module)
AppHost/        C# Aspire AppHost running Postgres + the PUBLIC runtime container
scripts/        build-wasm.sh, verify-no-local-sekiban-paths.sh (guard),
                smoke.sh, linux-build-check.sh
```

## Two-stage verification

**Stage 1 — pre-tag dry-run (NOT release evidence).** Tags
`v1.0.0-preview.1`–`.3` predate the root manifest, so the URL cannot resolve
until the operator cuts `v1.0.0-preview.4`. `smoke.sh --local-package` stages
the current repository tree, turns it into a local git repo tagged
`v1.0.0-preview.4`, and redirects the dependency via **SwiftPM dependency
mirroring**
(`swift package config set-mirror`, stored in
`.swiftpm/configuration/mirrors.json`) — the committed `Package.swift` is
never modified:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/smoke.sh --local-package
```

**Stage 2 — remote-version proof (release evidence).** After
`v1.0.0-preview.4` exists, the default mode clears any local redirection and
resolves the dependency from the real URL:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/smoke.sh
```

## Linux build feasibility

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/linux-build-check.sh
```

Runs `swift build` + `swift test` against the repository root inside a
`swift:6.x` Linux container; the outcome is recorded in
`docs/release/swift-sdk-release-lane.md`. (The consumer sample itself only
builds for the wasm target — its linker flags are wasm-ld specific — so the
package is the meaningful Linux target.)

Prerequisites: Docker, .NET SDK (AppHost), Swift 6.3+ with the
`swift-6.3.1-RELEASE_wasm` WebAssembly SDK (see
`build/scripts/build-swift-wasm.sh` for the toolchain layout).
