# Sekiban WASM Runtime Container — Public Local Runtime Contract

`docker/sekiban-wasm-runtime` packages the **Sekiban WASM Runtime Host** as a
local backend container. You provide a WASM module, a runtime manifest, and an
Event DB connection, and the container serves the serialized Sekiban DCB runtime
over HTTP — without you hosting Orleans manually.

This document is the public contract for the local runtime container: what it
provides, what it deliberately does **not** provide, and the exact inputs,
ports, and storage configuration it expects.

> This is an OSS **local runtime host**, not Sekiban Cloud. It does not issue
> credentials, manage tenants, or publish a hosted service.

## What This Container Provides

- A generic ASP.NET host (`Sekiban.Dcb.WasmRuntime.Host`) that runs serialized
  Sekiban DCB commands and queries.
- In-process WASM projector execution via Wasmtime, driven by a runtime
  manifest.
- Serialized HTTP endpoints for tag-state, commit, and query operations
  (see [Endpoints](#endpoints)).
- A `/health` endpoint for liveness/readiness checks.
- External event persistence. Postgres is the first-class local event store;
  SQLite and Cosmos DB are selectable via storage-provider configuration.
- A self-contained local Orleans silo: clustering, grain storage, and streams
  run **in-memory** inside the container (see
  [Runtime Topology](#runtime-topology)).

## What This Container Does Not Provide

- **No published registry image.** This slice does not publish a GHCR image and
  does not add an image-publish GitHub Actions workflow. You build the image
  locally from the repository (see [Build the Image](#build-the-image)).
- **No bundled Event DB.** Event persistence is external and configured by you.
  The container does not ship a production database; the compose sample starts a
  local Postgres only for convenience.
- **No Sekiban Cloud / SaaS behavior.** No serviceId issuance, no WorkOS, no
  management UI, no multi-tenant control plane, and no Cloud credential helpers.
- **No durable Orleans cluster.** Orleans clustering, grain storage, and streams
  are in-memory and local to a single container instance. They are not a durable
  or multi-node cluster.

## Runtime Inputs (Required)

| Input | How it is provided | Notes |
| --- | --- | --- |
| Runtime manifest | `SEKIBAN_MANIFEST_PATH` env var → path to a `sekiban-manifest.json` mounted into the container | Declares projectors, query mappings, and the default module path. See [Manifest](#manifest). |
| WASM module | `WASM_MODULE_PATH` env var → path to a `.wasm` module mounted into the container | Also referenced as `defaultModulePath` inside the manifest. |
| Event DB connection | `ConnectionStrings__SekibanDcb` env var → Postgres connection string | Required for the default Postgres provider. See [Storage Providers](#storage-providers). |

If `SEKIBAN_MANIFEST_PATH` is set, the manifest file must exist or the host fails
fast at startup. If no manifest is configured, the host falls back to a built-in
Weather manifest derived from `WASM_MODULE_PATH`; an explicit manifest is the
recommended, supported path.

## Ports

The host listens on **container port `8080`** (`ASPNETCORE_URLS=http://0.0.0.0:8080`,
`EXPOSE 8080`).

| Path | Host port | Container port |
| --- | --- | --- |
| `docker run` (this README) | `8080` | `8080` |
| `docker compose` sample (this directory) | `3000` | `8080` |

The compose sample also exposes Postgres on `localhost:5432` and DBGate on
`localhost:3001`.

## Volumes

The image creates two mount points for runtime inputs:

| Container path | Purpose |
| --- | --- |
| `/app/config` | Runtime manifest directory (mount `sekiban-manifest.json` here). |
| `/app/modules` | WASM module directory (mount your `.wasm` modules here). |

Mount both read-only (`:ro`) for a local runtime; the host only reads them.

## Environment Variables

### Required

| Variable | Description |
| --- | --- |
| `SEKIBAN_MANIFEST_PATH` | Absolute path (inside the container) to the runtime manifest JSON, e.g. `/app/config/sekiban-manifest.json`. |
| `WASM_MODULE_PATH` | Absolute path (inside the container) to the default `.wasm` module, e.g. `/app/modules/weather.wasm`. |
| `ConnectionStrings__SekibanDcb` | Postgres connection string for the external event store (required for the default Postgres provider). |

### Optional

| Variable | Default | Description |
| --- | --- | --- |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` | Bind address/port for the host. |
| `SEKIBAN_STORAGE_PROVIDER` | `postgres` | Event-store provider: `postgres`, `sqlite`, or `cosmos`. |
| `SEKIBAN_SQLITE_PATH` | host content root | SQLite database path when `SEKIBAN_STORAGE_PROVIDER=sqlite`. |
| `SEKIBAN_WASM_POOL_SIZE` | `1` | Pooled WASM instances per projector (~36 MB each). Set `0` for TinyGo/Go modules. |
| `SEKIBAN_PROJECTION_MODE` | `dual` | `dual`, `memory-only`, or `materialized-view-only`. |
| `SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB` | Wasmtime default | Upper bound (MB) for a Wasmtime instance's static linear-memory reservation. |
| `KEEP_TAG_PROJECTORS` | `false` | Keep tag-only projectors active in the MultiProjection grain when `true`. |

See [Memory Profiles](#memory-profiles) for how these settings map to small /
standard / large local memory profiles.

### Storage Providers

The provider is selected by `SEKIBAN_STORAGE_PROVIDER` (default `postgres`):

- **Postgres (default, first-class):** set `ConnectionStrings__SekibanDcb` to a
  Postgres connection string. If unset, the host defaults to
  `Host=127.0.0.1;Port=5432;Database=sekiban;Username=postgres;Password=postgres`,
  which is only useful when a Postgres instance is reachable at that address.
- **SQLite:** set `SEKIBAN_STORAGE_PROVIDER=sqlite` and optionally
  `SEKIBAN_SQLITE_PATH`. Intended for lightweight local use.
- **Cosmos DB:** set `SEKIBAN_STORAGE_PROVIDER=cosmos`,
  `ConnectionStrings__SekibanDcbCosmos`, and optionally
  `SEKIBAN_COSMOS_DATABASE` (default `SekibanDcb`).

Regardless of provider, **event persistence is external** to the container and
owned by you.

## Runtime Topology

The container runs a single self-contained Orleans silo with:

- `UseLocalhostClustering()` — local, single-node clustering.
- In-memory grain storage (`AddMemoryGrainStorage`).
- In-memory streams (`AddMemoryStreams`).

Only the **event store is durable and external** (Postgres by default). Orleans
clustering, grain storage, and streams are intentionally **in-memory** for local
runtime use, so projection caches and grain state do not survive a container
restart. This is the expected local-runtime assumption, not a limitation to work
around.

## Memory Profiles

Because Orleans and projections run in memory, the container memory limit is the
outer control and the runtime settings above are the inner controls. The
dominant inner cost is projection WASM (each active instance holds ~36 MB), so
memory scales roughly with `active projectors × SEKIBAN_WASM_POOL_SIZE × ~36 MB`
plus the base process and accumulating in-memory projection state.

| Profile | Container memory (start) | Suggested settings |
| --- | --- | --- |
| **small** | ~512 MB | `SEKIBAN_WASM_POOL_SIZE=0`; optionally `SEKIBAN_PROJECTION_MODE=materialized-view-only` (skips loading projection WASM). |
| **standard** | ~1–2 GB | Defaults (`SEKIBAN_PROJECTION_MODE=dual`, `SEKIBAN_WASM_POOL_SIZE=1`). |
| **large** | ~4 GB+ | `SEKIBAN_WASM_POOL_SIZE>=1`, `KEEP_TAG_PROJECTORS=true` only if needed; size from observed `processRssMB`. |

Set the outer limit with `docker run --memory=1g …` or Compose `mem_limit: 1g`,
and observe real usage with `GET /api/sekiban/memory-stats`
(`{ processRssMB, projectors[] }`). These are **observations, not guarantees** —
see [`docs/runtime-memory-profiles.md`](../../docs/runtime-memory-profiles.md)
for the full guide and [`docs/benchmark-results.md`](../../docs/benchmark-results.md)
for measured runs.

## Endpoints

| Method & Path | Purpose |
| --- | --- |
| `GET /` | Runtime info (provider, manifest default module, projectors, query mappings). |
| `GET /health` | Health check; returns `{ "status": "ok", ... }`. |
| `POST /api/sekiban/serialized/tag-state` | Read the current serialized tag state. |
| `POST /api/sekiban/serialized/tag-latest-sortable` | Read the latest sortable unique id for a tag. |
| `POST /api/sekiban/serialized/commit` | Commit serialized events with consistency tags. |
| `POST /api/sekiban/serialized/query` | Run a serialized single query. |
| `POST /api/sekiban/serialized/list-query` | Run a serialized list query. |

## Build the Image

The Dockerfile copies the whole repository as build context, so build from the
**repository root**:

```bash
docker build -f src/runtime/Sekiban.Dcb.WasmRuntime.Host/Dockerfile -t sekiban-wasm-runtime .
```

## Run with `docker run`

Minimal local run with a mounted manifest, a mounted `.wasm` module, and a
Postgres connection string. This assumes a Postgres instance is reachable (here,
on the host via `host.docker.internal`):

```bash
docker run --rm \
  -p 8080:8080 \
  -v "$PWD/docker/sekiban-wasm-runtime/config/sekiban-manifest.json:/app/config/sekiban-manifest.json:ro" \
  -v "$PWD/docker/sekiban-wasm-runtime/modules/weather.wasm:/app/modules/weather.wasm:ro" \
  -e SEKIBAN_MANIFEST_PATH=/app/config/sekiban-manifest.json \
  -e WASM_MODULE_PATH=/app/modules/weather.wasm \
  -e "ConnectionStrings__SekibanDcb=Host=host.docker.internal;Port=5432;Database=sekiban;Username=postgres;Password=postgres" \
  sekiban-wasm-runtime
```

Then check health:

```bash
curl http://localhost:8080/health
```

> Put your module at `docker/sekiban-wasm-runtime/modules/weather.wasm` first.
> For the Weather sample, copy it from an internal usage build, e.g.
> `cp src/internalUsages/cs/modules/csharp-weather.wasm docker/sekiban-wasm-runtime/modules/weather.wasm`.

## Published Image (GHCR Preview)

A preview image is published to GitHub Container Registry as:

```text
ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:<tag>
```

Pull a preview tag and run it instead of building locally — the runtime inputs
are identical to the [`docker run`](#run-with-docker-run) flow above (mounted
manifest, mounted `.wasm`, Postgres connection string):

```bash
docker pull ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1

docker run --rm \
  -p 8080:8080 \
  -v "$PWD/docker/sekiban-wasm-runtime/config/sekiban-manifest.json:/app/config/sekiban-manifest.json:ro" \
  -v "$PWD/docker/sekiban-wasm-runtime/modules/weather.wasm:/app/modules/weather.wasm:ro" \
  -e SEKIBAN_MANIFEST_PATH=/app/config/sekiban-manifest.json \
  -e WASM_MODULE_PATH=/app/modules/weather.wasm \
  -e "ConnectionStrings__SekibanDcb=Host=host.docker.internal;Port=5432;Database=sekiban;Username=postgres;Password=postgres" \
  ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1
```

The image is published by the
[`release-ghcr-image-preview`](../../.github/workflows/release-ghcr-image-preview.yml)
GitHub Actions workflow via manual `workflow_dispatch` (build-only by default;
push is opt-in). Image publishing is separate from NuGet publishing and uses the
built-in `GITHUB_TOKEN` with `packages: write` scoped to the publish job only.
The first published target is a Linux OCI image. If a tag has not been pushed
yet, build locally with [Build the Image](#build-the-image). See
[`docs/release/ghcr-image-preview.md`](../../docs/release/ghcr-image-preview.md)
for the publish procedure and tagging policy.

## Run with Docker Compose

The committed [`docker-compose.yml`](docker-compose.yml) starts the smallest
local stack — `runtime` + `postgres` (+ `dbgate` for inspection):

1. Put your Weather sample module at
   `docker/sekiban-wasm-runtime/modules/weather.wasm`.
2. Start the stack:

   ```bash
   cd docker/sekiban-wasm-runtime
   docker compose up --build
   ```

3. Open the runtime:

   - API root: `http://localhost:3000/`
   - Health: `http://localhost:3000/health`
   - DBGate: `http://localhost:3001/`
   - PostgreSQL: `localhost:5432`
   - Serialized tag state: `POST http://localhost:3000/api/sekiban/serialized/tag-state`
   - Serialized commit: `POST http://localhost:3000/api/sekiban/serialized/commit`
   - Serialized query: `POST http://localhost:3000/api/sekiban/serialized/query`
   - Serialized list query: `POST http://localhost:3000/api/sekiban/serialized/list-query`

The compose `runtime` service already wires `ConnectionStrings__SekibanDcb`,
`SEKIBAN_MANIFEST_PATH`, and `WASM_MODULE_PATH`, and mounts `./config` and
`./modules` into the container.

The published host ports default to `3000` (runtime), `5432` (Postgres), and
`3001` (DBGate), but are overridable to avoid conflicts (for example with a
running Aspire app on `3000`):

```bash
SEKIBAN_RUNTIME_PORT=18080 SEKIBAN_POSTGRES_PORT=15432 docker compose up --build
# runtime then on http://localhost:18080
```

### Compose: local build vs published image

By default the `runtime` service builds the image locally from the repository:

```yaml
  runtime:
    build:
      context: ../..
      dockerfile: src/runtime/Sekiban.Dcb.WasmRuntime.Host/Dockerfile
```

To run a published preview image instead of building, replace that `build:`
block with an `image:` reference (keep the rest of the service — `environment`,
`ports`, `volumes` — unchanged):

```yaml
  runtime:
    image: ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1
```

Then `docker compose pull runtime` (or `docker compose up` without `--build`)
uses the published image.

## Smoke Test

[`scripts/smoke-runtime-compose.sh`](../../scripts/smoke-runtime-compose.sh) is a
single-command local smoke that proves the container works as a real
event-sourced backend. It starts external Postgres + the runtime container (with
a mounted manifest and WASM module), commits a serialized `WeatherForecastCreated`
event through the container, reads it back via
`/api/sekiban/serialized/tag-latest-sortable`, then tears the stack down. It
prints a clear `PASS` / `FAIL` / `SKIP` result and writes
`reports/smoke/runtime-compose-smoke.md` (with container log tail on failure).

```bash
bash scripts/smoke-runtime-compose.sh
```

- Detects Docker or Podman (set `SMOKE_ENGINE=podman` to force). If no engine is
  available it records an explicit `SKIP` and exits 0.
- Resolves a WASM module in this order: `SMOKE_WASM_MODULE` → an existing
  `modules/weather.wasm` → the C# weather sample
  (`src/internalUsages/cs/modules/csharp-weather.wasm`) → builds it via
  `build/scripts/build-csharp-wasm.sh` (skip with `SMOKE_SKIP_BUILD=1`). If none
  can be obtained it records an explicit `SKIP`.
- Useful knobs: `SMOKE_RUNTIME_URL` (default `http://localhost:3000`),
  `SMOKE_HEALTH_TIMEOUT` (default `180`), `SMOKE_KEEP_UP=1` to leave the stack up.

## Container Engines

- **Docker** is the first-class, supported local engine for this container.
- **Podman** is an OCI compatibility target. The same `docker build` /
  `docker compose` flows are expected to work via `podman build` /
  `podman compose`; treat Podman as best-effort compatibility rather than the
  primary tested engine.
- **Apple container** and **Windows container** are future targets. They are not
  supported by this slice and are not expected to block local Docker usage.

## Manifest

`config/sekiban-manifest.json` is the runtime manifest for projector
registration and query routing.

The committed manifest is ready for the Weather sample:

- `WeatherForecastProjector`
- `WeatherForecastMultiProjection`
- `GetWeatherForecastCountQuery`
- `GetWeatherForecastListQuery`
- `WeatherForecastCreated`
- `WeatherForecastLocationUpdated`
- `WeatherForecastDeleted`

If another project uses different projector or query names, edit
`config/sekiban-manifest.json`.

## Notes

- The runtime can infer a default Weather manifest when `SEKIBAN_MANIFEST_PATH`
  is missing, but an explicit manifest is the recommended way to describe
  projectors and query mappings.
- In the compose sample, PostgreSQL is exposed on `localhost:5432` with
  `postgres/postgres` and database `sekiban`.
- In the compose sample, DBGate is exposed on `localhost:3001` and is
  preconfigured with the `weather` PostgreSQL connection.
