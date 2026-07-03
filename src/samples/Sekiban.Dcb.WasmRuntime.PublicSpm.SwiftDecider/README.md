# Swift SPM External-Consumer Sample (SWR-G063)

This sample is the **Swift external-consumer proof** for SekibanWasmRuntime:
its committed `Package.swift` depends on the public mirror
`https://github.com/J-Tech-Japan/sekiban-swift` at **exact 0.1.0** — no
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
Package.swift   Depends on the public sekiban-swift mirror at exact 0.1.0
Sources/        WeatherForecast domain + C-ABI entry points (wasm module)
AppHost/        C# Aspire AppHost running Postgres + the PUBLIC runtime container
scripts/        build-wasm.sh, verify-no-local-sekiban-paths.sh (guard),
                smoke.sh, linux-build-check.sh
```

## Two-stage verification

**Stage 1 — pre-publish dry-run (NOT release evidence).** Until the
sekiban-swift mirror is public with tag v0.1.0, the URL cannot resolve.
`smoke.sh --local-package` stages the mirror tree with the SWR-G062 sync
dry-run, turns it into a local git repo tagged `v0.1.0`, and redirects the
dependency via **SwiftPM dependency mirroring**
(`swift package config set-mirror`, stored in
`.swiftpm/configuration/mirrors.json`) — the committed `Package.swift` is
never modified:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/smoke.sh --local-package
```

**Stage 2 — mirror-resolved proof (release evidence).** After the mirror is
public at v0.1.0, the default mode clears any mirror redirection and resolves
the dependency from the real URL:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/smoke.sh
```

## Linux build feasibility

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/linux-build-check.sh
```

Stages the mirror tree and runs `swift build` + `swift test` inside a
`swift:6.x` Linux container; the outcome is recorded in
`docs/release/swift-sdk-release-lane.md`. (The consumer sample itself only
builds for the wasm target — its linker flags are wasm-ld specific — so the
package is the meaningful Linux target.)

Prerequisites: Docker, .NET SDK (AppHost), Swift 6.3+ with the
`swift-6.3.1-RELEASE_wasm` WebAssembly SDK (see
`build/scripts/build-swift-wasm.sh` for the toolchain layout).
