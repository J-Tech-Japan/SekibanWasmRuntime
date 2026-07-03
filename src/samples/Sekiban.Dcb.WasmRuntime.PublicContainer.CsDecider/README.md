# Public Container C# Decider Sample (SWR-G036)

This sample is the **public artifact consumption proof** for SekibanWasmRuntime.
It runs SekibanWasmRuntime exactly as an external developer would:

- **public NuGet packages** for the Decider domain (`Sekiban.Dcb.WithoutResult`,
  the same `10.2.x` contract line the runtime image is built on) — **not**
  repo-local library project references;
- the **public GHCR runtime container** `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`
  (the latest verified runtime-host tag — distinct from the public NuGet package
  versions, which are `1.0.0-preview.1`) — **not**
  `AddProject<...WasmRuntime.Host>`;
- a small **Decider-pattern** weather domain compiled to WASM;
- **Postgres** as the external event DB;
- an AppHost that wires the runtime container through the
  `Sekiban.Dcb.WasmRuntime.Aspire` package's single `AddSekibanWasmRuntime`
  call: the public image, read-only `.wasm`/manifest bind mounts, the runtime
  environment contract, and the Postgres references (see
  [`docs/nuget/aspire-package-readme.md`](../../../docs/nuget/aspire-package-readme.md)).

> **This sample must not use repository-local implementation shortcuts.** Its whole
> point is to prove that the published packages + published image are sufficient.
> It does not reference `src/runtime`, `src/internalUsages`, or `submodules/Sekiban`
> source. The `ProjectReference`s are sample-internal (`Wasm` → `Domain`: the
> domain is compiled *into* the WASM module) plus the packable
> `Sekiban.Dcb.WasmRuntime.Aspire` project, which stands in for its NuGet package
> until the first publish — neither is a runtime/source shortcut.

## Layout

```text
Domain/    Weather Decider domain (events, command, tag, projector, multi-projector,
           query) on the public Sekiban.Dcb.WithoutResult package.
Wasm/      NativeAOT-LLVM wasi-wasm reactor exposing the runtime ABI for the domain.
Wasm/MaterializedView/  WeatherForecast materialized view (mv_metadata / mv_initialize /
           mv_apply_event) — emits SQL the host runs against DcbMaterializedViewPostgres.
AppHost/   Aspire AppHost: public GHCR runtime image + Postgres (event store +
           materialized-view DB) + read-only mounts.
scripts/   build-wasm.sh (build module + manifest), smoke.sh (live commit/read/query/MV).
```

## Run it

Prerequisites: Docker (first-class local engine), the .NET 10 SDK, and (on
non-Linux hosts) Docker is also used to cross-build the WASM module.

```bash
# 1. Build the WASM module + manifest into the artifact path.
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh

# 2. Start the AppHost (Postgres + the public runtime container).
dotnet run --project src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/AppHost

# Or run the end-to-end smoke (build if needed, start, commit + read + query, tear down):
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
```

Generated outputs are git-ignored under
`artifacts/samples/public-container-cs-decider/` (`modules/*.wasm`, `config/sekiban-manifest.json`).

## Smoke

[`scripts/smoke.sh`](scripts/smoke.sh) starts the AppHost, waits for the runtime
container `/health`, then for **`/ready`** (schema-aware: it fails closed until
the DCB Postgres schema exists, so the smoke never proceeds to commit against a
schema-less database), confirms it is the Sekiban runtime, then proves the full
path through the **public** container:

1. `POST /api/sekiban/serialized/commit` — a `WeatherForecastCreated` event;
2. `POST /api/sekiban/serialized/tag-latest-sortable` — tag-state read back;
3. `POST /api/sekiban/serialized/list-query` — `GetWeatherForecastListQuery`
   (exercises projection catch-up, which loads the WASI **preview2 shim**; a
   runtime image missing `libwasmtime_preview2_shim.so` fails here with
   `DllNotFoundException: ... 'wasmtime_preview2_shim'`).
4. **Materialized view read** — confirms the `WeatherForecast` MV caught the
   committed event up into `DcbMaterializedViewPostgres` (read directly from
   Postgres; see [Materialized View](#materialized-view)).

It writes `reports/smoke/public-container-cs-decider-smoke.md` (`PASS` / `FAIL` /
`SKIP`) and tears the stack down. On failure it captures the HTTP response body
and the runtime container logs, and it talks to the runtime with `curl -q` so a
user `~/.curlrc` cannot break it. The runtime host now runs EF migration for
Postgres at startup so `dcb_events` exists before the first commit — see
[`docs/release/runtime-host-postgres-schema-smoke.md`](../../../docs/release/runtime-host-postgres-schema-smoke.md)
for the root-cause classification and the preview 2 republish requirement.

## Materialized View

The sample also demonstrates a **Materialized View (MV)** through the same public
artifacts. The WASM module exports `mv_metadata` / `mv_initialize` / `mv_apply_event`
([`Wasm/MaterializedView/`](Wasm/MaterializedView/)) for a single-table
`WeatherForecast` view, and the generated manifest declares it under
`materializedViews`.

- **Projection mode**: the AppHost sets `SEKIBAN_PROJECTION_MODE=dual` (the
  default), which runs both the in-memory MultiProjection path *and* the
  materialized-view runtime. Other values: `materialized-view-only`, `memory-only`.
- **Connection strings**: `SekibanDcb` is the event store; **`DcbMaterializedViewPostgres`**
  is the MV registry + state DB. The runtime activates the WASM MV executor only
  when the manifest declares `materializedViews`, the mode is `dual` /
  `materialized-view-only`, **and** `DcbMaterializedViewPostgres` is configured.
- **Reads are caller-owned**: the generic runtime host exposes **no** MV read API.
  After commit, the host's MV catch-up worker applies the event to the WASM
  projector, which writes the row to the physical table named in the
  `sekiban_mv_registry` table. A caller (here, `scripts/smoke.sh`) reads MV state
  **directly from `DcbMaterializedViewPostgres`** — exactly as an external app
  would with its own Postgres driver.

> **Live MV verification requires a runtime image that carries the WASI preview2
> shim.** The MV catch-up uses the same preview2 component path as `list-query`,
> so it needs `libwasmtime_preview2_shim.so` (SWR-G042). The published
> `1.0.0-preview.2` tag predates that fix and is shim-less. **The verified,
> recommended public tag is `1.0.0-preview.3`** — a multi-arch, shim-carrying image
> (digest `sha256:8bdebccd…`) that this sample now defaults to. The full smoke
> passes end-to-end against it (`/health`, schema-aware `/ready`, command commit,
> tag-state read, `list-query`, and Materialized View catch-up):
> `SAMPLE_RUNTIME_IMAGE_TAG=1.0.0-preview.3 bash scripts/smoke.sh`. See
> [`docs/release/runtime-host-preview-3-release-verification.md`](../../../docs/release/runtime-host-preview-3-release-verification.md)
> (the public-artifact verification evidence) and
> [`docs/release/runtime-host-preview-3-release-metadata.md`](../../../docs/release/runtime-host-preview-3-release-metadata.md)
> (the release plan it verifies).

## Troubleshooting

- **`Missing WASM module or manifest`** on AppHost start → run `scripts/build-wasm.sh`
  first; the AppHost fails closed when the mounted inputs are absent.
- **No `.wasm` produced** → the build uses NativeAOT-LLVM (`wasi-wasm`) via Docker
  `linux/amd64` + WASI SDK 29 (see `scripts/build-wasm.sh`). Ensure Docker is
  running; on Linux it can build natively with the WASI SDK on `PATH`.
- **`docker pull` of the runtime image fails** → confirm
  `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` is reachable.
- **`no matching manifest for linux/arm64/v8`** on Apple Silicon → this only
  affects **older amd64-only** tags such as `1.0.0-preview.1`. The sample now
  defaults to **`1.0.0-preview.3`** — the verified multi-arch + preview2-shim tag —
  so a plain pull works on arm64 with **no** `DOCKER_DEFAULT_PLATFORM=linux/amd64`
  override. Avoid `1.0.0-preview.2` (multi-arch but **shim-less**, so list-query /
  MV fail). Pin a specific published tag with `SAMPLE_RUNTIME_IMAGE_TAG=<tag>`;
  verify a tag's platforms with `docker buildx imagetools inspect <image>:<tag>`.
  See [`docs/release/runtime-host-preview-3-release-verification.md`](../../../docs/release/runtime-host-preview-3-release-verification.md).
- **Port already in use** → the smoke picks a free host port; for a manual run,
  Aspire assigns one (see the dashboard), or set `SAMPLE_RUNTIME_HOST_PORT`.

## Notes

- The repo pins the Aspire `13.2.x` line; an external consumer would use their
  own Aspire version. The container wiring (`AddContainer` + bind mounts +
  connection string) is the stable part being demonstrated.
- The generated `.wasm` is intentionally **not** checked in (regenerate with the
  build script).
