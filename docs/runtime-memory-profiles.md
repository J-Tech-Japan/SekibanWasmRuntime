# Runtime Container Memory Profiles

This guide gives a simple operating model for how much memory to allocate to the
Sekiban WASM Runtime Host container and which runtime settings affect memory use.

It complements the
[local runtime container contract](../docker/sekiban-wasm-runtime/README.md).

> **These are observations and guidance, not guarantees.** Actual memory use
> depends on your manifest (number of projectors), workload (events committed and
> queried), and WASM module. Treat any number here — and the figures in
> [`docs/benchmark-results.md`](benchmark-results.md) — as observed behavior, not
> a guaranteed ceiling. Measure your own workload with
> [`/api/sekiban/memory-stats`](#observing-memory) and set limits with headroom.

## How runtime memory is spent

- **Event persistence is external.** Committed events live in your Postgres (or
  other configured) event store, not in container memory.
- **Orleans runs in-memory.** Clustering, grain storage, and streams are
  in-memory and local to the container. In-memory MultiProjection grains
  accumulate projected state as they process events, so RSS grows with the number
  of events a projection has folded in.
- **Projection WASM is the dominant inner cost.** Each active projection WASM
  instance holds roughly **~36 MB** of WASM linear memory. Total ≈
  `active projectors × pooled instances per projector × ~36 MB`, plus the base
  host process and the growing in-memory projection state.
- **The container memory limit is the outer control; the runtime settings below
  are the inner controls.**

## Inner controls (environment variables)

| Variable | Default | Memory effect |
| --- | --- | --- |
| `SEKIBAN_PROJECTION_MODE` | `dual` | `dual` wires both the in-memory MultiProjection grain and the materialized view. `memory-only` skips the materialized view. `materialized-view-only` makes the MultiProjection query endpoints return `503` so the `MultiProjectionGrain` never activates and **no projection WASM is loaded** — the lowest-memory mode, at the cost of in-memory MultiProjection queries. |
| `SEKIBAN_WASM_POOL_SIZE` | `1` | Idle pooled WASM instances kept per projector (each ~36 MB). `1` keeps one warm instance per projector for reuse. `0` keeps none (lower idle memory; required for TinyGo/Go modules). Higher values trade memory for concurrency. |
| `SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB` | Wasmtime default | Upper bound (MB) for a Wasmtime instance's static linear memory reservation. Leave unset to use the Wasmtime default; set a value only to cap/raise the reservation deliberately. |
| `KEEP_TAG_PROJECTORS` | `false` | When `false`, tag-only projectors (not mapped to any query) are removed from the MultiProjection grain to save ~36 MB each. Set `true` only if you need those projectors active. |

Tag-state reads (`/api/sekiban/serialized/tag-state`) and commits do not require
the MultiProjection grain; only the serialized **query** / **list-query**
endpoints activate it (and therefore load projection WASM). This is why
`materialized-view-only` is the smallest-footprint mode for commit + tag-state
workloads.

## Profiles

Pick a starting point and adjust from your own measurements. "Container memory"
is the outer limit you pass to Docker/Compose (see [examples](#setting-a-memory-limit)).

| Profile | Container memory (starting point) | Typical use | Suggested settings |
| --- | --- | --- | --- |
| **small** | ~512 MB | One projector; commit + tag-state; little or no in-memory MultiProjection querying. | `SEKIBAN_WASM_POOL_SIZE=0`; consider `SEKIBAN_PROJECTION_MODE=materialized-view-only` to skip loading projection WASM entirely. |
| **standard** | ~1–2 GB | The default. A handful of projectors with MultiProjection queries. | Defaults: `SEKIBAN_PROJECTION_MODE=dual`, `SEKIBAN_WASM_POOL_SIZE=1`. |
| **large** | ~4 GB+ | Many projectors and/or heavier query/throughput; headroom for growing in-memory projection state. | `SEKIBAN_WASM_POOL_SIZE>=1` (raise for concurrency), `KEEP_TAG_PROJECTORS=true` only if needed; size the limit from observed `processRssMB` under load. |

These tiers scale with the dominant cost (`active projectors × pool size × ~36 MB`)
plus the base process and accumulating in-memory projection state. A single large
WASM module (the C# weather sample is ~35 MB on disk) and a heavy event backlog
push you toward the **large** tier; a minimal single-projector commit/tag-state
backend fits **small**.

## Setting a memory limit

**`docker run`:**

```bash
docker run --rm \
  --memory=1g --memory-swap=1g \
  -p 8080:8080 \
  -v "$PWD/docker/sekiban-wasm-runtime/config/sekiban-manifest.json:/app/config/sekiban-manifest.json:ro" \
  -v "$PWD/docker/sekiban-wasm-runtime/modules/weather.wasm:/app/modules/weather.wasm:ro" \
  -e SEKIBAN_MANIFEST_PATH=/app/config/sekiban-manifest.json \
  -e WASM_MODULE_PATH=/app/modules/weather.wasm \
  -e SEKIBAN_WASM_POOL_SIZE=1 \
  -e "ConnectionStrings__SekibanDcb=Host=host.docker.internal;Port=5432;Database=sekiban;Username=postgres;Password=postgres" \
  sekiban-wasm-runtime
```

**Docker Compose** — add a limit (and any inner-control env vars) to the
`runtime` service:

```yaml
  runtime:
    # ...build/image, environment, ports, volumes as in docker-compose.yml...
    mem_limit: 1g            # outer control (Compose v2 standalone limit)
    environment:
      SEKIBAN_WASM_POOL_SIZE: "1"
      SEKIBAN_PROJECTION_MODE: "dual"
```

For a small profile, set `mem_limit: 512m` and
`SEKIBAN_PROJECTION_MODE: "materialized-view-only"` / `SEKIBAN_WASM_POOL_SIZE: "0"`;
for large, raise `mem_limit` (e.g. `4g`) after checking `processRssMB` under load.

## Observing memory

`GET /api/sekiban/memory-stats` reports the process resident set size and
per-projector event counts:

```bash
curl http://localhost:8080/api/sekiban/memory-stats
```

```json
{
  "processRssMB": 612,
  "projectors": [
    { "projectorName": "WeatherForecastMultiProjection", "eventsProcessed": 12345 }
  ]
}
```

Use `processRssMB` under your real workload to size the container limit with
headroom, and watch how it grows with `eventsProcessed` to decide whether a
profile needs more memory or a different projection mode.
