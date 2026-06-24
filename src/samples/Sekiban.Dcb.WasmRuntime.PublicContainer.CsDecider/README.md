# Public Container C# Decider Sample (SWR-G036)

This sample is the **public artifact consumption proof** for SekibanWasmRuntime.
It runs SekibanWasmRuntime exactly as an external developer would:

- **public NuGet packages** for the Decider domain (`Sekiban.Dcb.WithoutResult`,
  the same `10.2.x` contract line the runtime image is built on) — **not**
  repo-local library project references;
- the **public GHCR runtime container** `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1`
  via Aspire `AddContainer` — **not** `AddProject<...WasmRuntime.Host>`;
- a small **Decider-pattern** weather domain compiled to WASM;
- **Postgres** as the external event DB;
- an AppHost that mounts the generated `.wasm` and manifest into the runtime
  container read-only and wires the DB connection string.

> **This sample must not use repository-local implementation shortcuts.** Its whole
> point is to prove that the published packages + published image are sufficient.
> It does not reference `src/runtime`, `src/internalUsages`, or `submodules/Sekiban`
> source. The only `ProjectReference` is sample-internal (`Wasm` → `Domain`): the
> domain is compiled *into* the WASM module, which is normal sample composition,
> not a runtime/source shortcut.

## Layout

```text
Domain/    Weather Decider domain (events, command, tag, projector, multi-projector,
           query) on the public Sekiban.Dcb.WithoutResult package.
Wasm/      NativeAOT-LLVM wasi-wasm reactor exposing the runtime ABI for the domain.
AppHost/   Aspire AppHost: public GHCR runtime image + Postgres + read-only mounts.
scripts/   build-wasm.sh (build module + manifest), smoke.sh (live commit/read/query).
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
container `/health`, confirms it is the Sekiban runtime, then proves the full
path through the **public** container:

1. `POST /api/sekiban/serialized/commit` — a `WeatherForecastCreated` event;
2. `POST /api/sekiban/serialized/tag-latest-sortable` — tag-state read back;
3. `POST /api/sekiban/serialized/list-query` — `GetWeatherForecastListQuery`.

It writes `reports/smoke/public-container-cs-decider-smoke.md` (`PASS` / `FAIL` /
`SKIP`) and tears the stack down.

## Troubleshooting

- **`Missing WASM module or manifest`** on AppHost start → run `scripts/build-wasm.sh`
  first; the AppHost fails closed when the mounted inputs are absent.
- **No `.wasm` produced** → the build uses NativeAOT-LLVM (`wasi-wasm`) via Docker
  `linux/amd64` + WASI SDK 29 (see `scripts/build-wasm.sh`). Ensure Docker is
  running; on Linux it can build natively with the WASI SDK on `PATH`.
- **`docker pull` of the runtime image fails** → confirm
  `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1` is reachable.
- **`no matching manifest for linux/arm64/v8`** on Apple Silicon → the pinned
  `1.0.0-preview.1` tag is an **older amd64-only** image. Run the sample with the
  amd64 emulation workaround: `export DOCKER_DEFAULT_PLATFORM=linux/amd64` before
  `dotnet run`/`scripts/smoke.sh`. Once **preview 2** (`1.0.0-preview.2`, the
  first multi-arch runtime-host tag — `linux/amd64` + `linux/arm64`) is published,
  repoint the AppHost `RuntimeImageTag` to it and drop the override; verify with
  `docker buildx imagetools inspect <image>:<tag>`. See
  [`docs/release/runtime-host-preview-2-release-checklist.md`](../../../docs/release/runtime-host-preview-2-release-checklist.md).
- **Port already in use** → the smoke picks a free host port; for a manual run,
  Aspire assigns one (see the dashboard), or set `SAMPLE_RUNTIME_HOST_PORT`.

## Notes

- The repo pins the Aspire `13.2.x` line; an external consumer would use their
  own Aspire version. The container wiring (`AddContainer` + bind mounts +
  connection string) is the stable part being demonstrated.
- The generated `.wasm` is intentionally **not** checked in (regenerate with the
  build script).
