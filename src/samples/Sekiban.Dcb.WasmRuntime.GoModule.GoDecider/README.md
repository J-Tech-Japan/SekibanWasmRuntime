# Go Published-Module Consumer Sample (SWR-G061)

This sample is the **Go external-consumer proof** for SekibanWasmRuntime: it
consumes the Go SDK as the published subdirectory module
`github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go` — the committed
`go.mod` has **no replace directives and no local Sekiban paths** — and proves
the four consumer checks against the **public GHCR runtime container**
(`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`):

1. **Command execution** — `CreateWeatherForecast` + `UpdateWeatherForecastLocation`
   through the typed `client.SekibanRuntimeClient` command flow.
2. **Tag-state readback** — the updated location is visible in the tag state.
3. **In-memory projection query** — `GetWeatherForecastListQuery` and
   `GetWeatherForecastCountQuery` against the WASM multi-projection.
4. **Materialized-view catch-up/read** — the `WeatherForecast` MV row (with the
   updated location) appears in `DcbMaterializedViewPostgres`.

The domain mirrors the crates.io Rust sample (`CratesIo.RsDecider`) so the
evidence is comparable across SDK languages: the same events, the same MV SQL,
the same manifest shape.

## Layout

```text
domain/   WeatherForecast events/states, client-side commands, and the MV projector
wasm/     TinyGo WASI reactor exposing the projector ABI (built by scripts/build-wasm.sh)
client/   Typed Go client driving command/tag-state/query through the runtime HTTP contract
AppHost/  C# Aspire AppHost running Postgres + the PUBLIC runtime container
scripts/  build-wasm.sh, verify-no-local-sekiban-paths.sh (guard), smoke.sh
go.work   Dev-time overlay (see Two-stage verification below)
```

## Two-stage verification

**Stage 1 — pre-publish dry-run (NOT release evidence).** Until the
`src/lib/sekiban-go/v0.1.0` tag exists, the published module cannot be fetched.
`smoke.sh --local-module` builds through the repo-committed `go.work` overlay,
whose `replace` points at the in-repo SDK — the committed `go.mod` is never
modified. This validates the sample end-to-end before the first tag.

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.GoModule.GoDecider/scripts/smoke.sh --local-module
```

**Stage 2 — published-module proof (release evidence).** After the tag is
pushed, the default mode runs with `GOWORK=off`, so the SDK resolves only as
the published module through the Go module proxy:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.GoModule.GoDecider/scripts/smoke.sh
```

(One-time after the tag: run `GOWORK=off go mod tidy` in the sample and commit
the `go.sum` update — plain `go mod tidy` before the tag exists would drop the
unresolvable require.)

The dependency guard (`scripts/verify-no-local-sekiban-paths.sh`) fails on any
replace directive or local Sekiban path in the committed `go.mod` and asserts
the AppHost targets the public GHCR image.

Prerequisites: Docker, .NET SDK (AppHost), Go 1.22+, TinyGo.
