# MoonBit Mooncakes External-Consumer Sample (SWR-G065)

This sample is the **MoonBit external-consumer proof** for SekibanWasmRuntime:
its committed `moon.mod.json` manifests declare `sekiban/sekiban-wasm-runtime`
and `sekiban/sekiban-client` as **mooncakes.io registry dependencies** — no
local path resolution (guarded by `scripts/verify-no-local-sekiban-paths.sh`) —
and it proves the four consumer checks against the **public GHCR runtime
container** (`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`):

1. **Command execution** — `WeatherForecastCreated` +
   `WeatherForecastLocationUpdated` commits through the typed
   `sekiban-client` HTTP client.
2. **Tag-state readback** — the projected tag state shows the updated location.
3. **In-memory projection query** — `GetWeatherForecastListQuery` +
   `GetWeatherForecastCountQuery` via the client package.
4. **Materialized-view catch-up/read** — the `WeatherForecast` MV row (updated
   location) appears in `DcbMaterializedViewPostgres`.

The domain mirrors the Rust/Go/Swift published-artifact samples (same events,
same MV SQL, same manifest shape) so the evidence is comparable across SDK
languages. The sample exercises **both** packages: `wasm/` builds the
projector module with `sekiban/sekiban-wasm-runtime`; `client/` drives the
runtime with `sekiban/sekiban-client`.

## Layout

```text
wasm/     Projector module (registry dep: sekiban/sekiban-wasm-runtime, target wasm)
client/   Typed client CLI (registry dep: sekiban/sekiban-client, target native)
AppHost/  C# Aspire AppHost running Postgres + the PUBLIC runtime container
scripts/  build-wasm.sh, verify-no-local-sekiban-paths.sh (guard), smoke.sh
```

## Two-stage verification

**Stage 1 — pre-publish dry-run (NOT release evidence).** Until the sekiban
packages are published to mooncakes.io (human-gated account/scope batch),
registry resolution cannot succeed. `smoke.sh --local-packages` builds a
**staged copy** of the sample under `artifacts/` whose manifests are rewritten
to path dependencies on `src/lib/sekiban-moonbit` — the committed manifests
are never modified (MoonBit has no workspace/mirror overlay mechanism, so the
staged copy is the redirection boundary):

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.Mooncakes.MbDecider/scripts/smoke.sh --local-packages
```

**Stage 2 — registry-resolved proof (release evidence).** After
`sekiban/sekiban-wasm-runtime` and `sekiban/sekiban-client` are published, the
default mode builds the committed sample directly from mooncakes.io:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.Mooncakes.MbDecider/scripts/smoke.sh
```

Note: `moon.work.json` at the sample root makes
`(cd <sample> && moon check)` a valid workspace command covering both
modules. Pre-publish it fails with `module was not found in the registry`
(registry resolution has no overlay) — the documented acceptable limitation;
the staged-copy build inside `smoke.sh --local-packages` is the runnable
equivalent, and after the mooncakes publish the same root command becomes the
registry-resolved follow-up check.

Prerequisites: Docker, .NET SDK (AppHost), the `moon` toolchain.
