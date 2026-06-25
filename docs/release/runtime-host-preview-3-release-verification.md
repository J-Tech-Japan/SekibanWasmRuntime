# Runtime Host Preview 3 Public Verification (SWR-G045)

This is the **post-publish verification record** for the public runtime-host image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`. SWR-G044 prepared
the release metadata and publish plan; the operator then published preview 3 from
merged `main`; this slice verifies the **actual published artifact** (GHCR bytes +
public-container sample smoke) and flips the docs/sample wording from planned to
verified.

> **Release readiness IS claimed for `1.0.0-preview.3`.** The exact tag is a
> published multi-arch, shim-carrying manifest list, the moving `preview` tag points
> at the same digest, and the public-container sample smoke passes end-to-end against
> the exact public tag including `list-query` and Materialized View catch-up.

## Publish run evidence

| Field | Value |
| --- | --- |
| Workflow | `release-ghcr-image-preview` (`workflow_dispatch`, `push=true`) |
| Run URL | <https://github.com/J-Tech-Japan/SekibanWasmRuntime/actions/runs/28160428104> |
| Status / conclusion | `completed` / **`failure`** (see note) |
| Source commit (`headSha`) | `9441260ed214de88e1e9d816618f2490c059ff72` |
| Published digest | `sha256:8bdebccdd81d02bc958bcf422eea5ffbafd3f2cc2eec5fe97c4b7129a16db79f` |
| Platforms | `linux/amd64` + `linux/arm64` (manifest list) |

```bash
gh run view 28160428104 --repo J-Tech-Japan/SekibanWasmRuntime \
  --json status,conclusion,headSha,url
# {"status":"completed","conclusion":"failure",
#  "headSha":"9441260ed214de88e1e9d816618f2490c059ff72",
#  "url":".../actions/runs/28160428104"}
```

**Note on the `failure` conclusion — the published bytes are nonetheless correct.**
The publish job ran `09:26:11Z → 12:06:02Z` (~160 min) and hit its time budget
during the QEMU-emulated `linux/arm64` leg of the multi-arch build/push: the
"Build and push" step and the workflow's own "Verify multi-arch manifest list"
step did not record success, so the job is marked `failure`. The multi-arch
manifest (both platforms, both native libraries) had already been pushed to GHCR,
so the exact tag and the moving `preview` tag both resolve to the corrected digest.
**This verification does not trust the run conclusion** — it independently inspects
the published bytes below (fail-closed), which is the whole point of splitting
publish from verification.

## Verification results (all PASS)

### 1. Multi-arch manifest + per-platform native libraries (fail closed)

`scripts/release/verify-runtime-host-multiarch.sh` against `1.0.0-preview.3` →
**PASS** (`artifacts/release/runtime-host-multiarch-verification.md`):

```text
- OK: manifest list contains linux/amd64
- OK: manifest list contains linux/arm64
- OK: linux/amd64 pull succeeded
- OK: linux/amd64: /app/libwasmtime.so present and arch-correct (e_machine=3e00)
- OK: linux/amd64: /app/libwasmtime_preview2_shim.so present and arch-correct (e_machine=3e00)
- OK: linux/arm64 pull succeeded
- OK: linux/arm64: /app/libwasmtime.so present and arch-correct (e_machine=b700)
- OK: linux/arm64: /app/libwasmtime_preview2_shim.so present and arch-correct (e_machine=b700)
```

```bash
IMAGE_TAG=1.0.0-preview.3 scripts/release/verify-runtime-host-multiarch.sh
```

### 2. Moving `preview` tag points at the preview 3 digest

```bash
docker buildx imagetools inspect ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3 \
  --format '{{.Manifest.Digest}}'
# sha256:8bdebccdd81d02bc958bcf422eea5ffbafd3f2cc2eec5fe97c4b7129a16db79f
docker buildx imagetools inspect ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:preview \
  --format '{{.Manifest.Digest}}'
# sha256:8bdebccdd81d02bc958bcf422eea5ffbafd3f2cc2eec5fe97c4b7129a16db79f   (MATCH)
```

For contrast, `1.0.0-preview.2` remains at the stale shim-less digest
`sha256:0d5c4fe1…` — it was **not** re-pointed by this release.

### 3. Public-container sample smoke against the exact public tag

`SAMPLE_RUNTIME_IMAGE_TAG=1.0.0-preview.3` build + smoke →
**PASS** (`reports/smoke/public-container-cs-decider-smoke.md`). The smoke exercises
the published image end-to-end through the Aspire AppHost (Postgres + the public
runtime container, the WASM module + manifest mounted):

| Check | Result |
| --- | --- |
| `/health` | ✅ |
| schema-aware `/ready` (`dcb schema present`) | ✅ |
| runtime identity (`Sekiban WASM Runtime Host`) | ✅ |
| command commit (`WeatherForecastCreated`) | ✅ |
| tag-state read (`tag-latest-sortable`) | ✅ |
| `list-query` (`GetWeatherForecastListQuery`) — proves the preview2 shim loads | ✅ |
| Materialized View catch-up (`DcbMaterializedViewPostgres`) | ✅ |

```bash
SAMPLE_RUNTIME_IMAGE_TAG=1.0.0-preview.3 \
  bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh
SAMPLE_RUNTIME_IMAGE_TAG=1.0.0-preview.3 \
  bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
# [smoke] materialized-view read OK (table=sekiban_mv_weatherforecast_v1_weather_forecast location=Kyoto)
# [smoke] PASS: commit + tag-state read + list-query + materialized-view read all succeeded ...
```

The MV row was independently confirmed in `DcbMaterializedViewPostgres`: a committed
`WeatherForecastCreated` event was caught up into the physical table
`sekiban_mv_weatherforecast_v1_weather_forecast` (registered in `sekiban_mv_registry`)
within seconds — the host has no MV read API by design, so a caller reads MV state
directly from Postgres.

## Observations / non-blocking notes

- **Transient MV grain-activation warning (self-heals).** On startup in `dual` mode
  the host runs **both** a Postgres hosted catch-up worker **and** an Orleans MV
  grain activator; both call `PostgresMvRegistryStore.EnsureInfrastructureAsync`, and
  when they race on first-time table/type creation Postgres logs
  `23505 duplicate key value violates unique constraint "pg_type_typname_nsp_index"`
  (`MaterializedViewGrainActivator: Failed to activate ... WeatherForecast/1`). This
  is **not a release blocker**: the MV infrastructure is still created and MV catch-up
  still completes (verified: the row appears within ~8 s). It is a pre-existing
  concurrency wrinkle in the `Sekiban.Dcb.MaterializedView.Postgres` infrastructure
  setup, not something introduced by preview 3, and is **out of scope** for this
  verification slice (it cannot be changed in an already-published image). Tracked as
  a follow-up to make `EnsureInfrastructureAsync` idempotent under concurrency.
- **Smoke MV-check robustness fix (in this PR).** The sample smoke's Materialized
  View check previously selected the Aspire Postgres container with
  `docker ps --filter ancestor=postgres | head -1` and ran `psql -U postgres` with no
  password. With other Postgres containers running concurrently it picked the wrong
  one, and Aspire's generated `POSTGRES_PASSWORD` made the unauthenticated `psql`
  fail silently (`2>/dev/null`), producing a **false** "DcbMaterializedViewPostgres
  database was not created". The smoke now probes candidate containers (Aspire
  `pg-*` first) and keeps the one whose server actually exposes a
  `dcbmaterializedview%` database, authenticating with that container's
  `POSTGRES_PASSWORD`. This is sample/verification tooling only — no runtime-host
  behavior changed.

## Status checklist

- [x] Publish run URL, conclusion, source commit, and digest recorded (above).
- [x] `1.0.0-preview.3` exists as a public `linux/amd64` + `linux/arm64` manifest list.
- [x] Both platform images pull and contain arch-correct `/app/libwasmtime.so` and
  `/app/libwasmtime_preview2_shim.so`.
- [x] Moving `preview` digest == `1.0.0-preview.3` digest (`sha256:8bdebccd…`).
- [x] Public-container smoke PASS through `/health`, schema-aware `/ready`, identity,
  command commit, tag-state read, `list-query`, and Materialized View read/catch-up.
- [x] Docs/sample wording flipped from planned/not-yet-published to verified +
  recommended (`docs/quickstart.md`, the docker README, the sample README, the
  AppHost default tag).

## Closeout writeback (G461)

Splitting the work into **SWR-G044 (release metadata + publish plan)**, the
**operator-gated GHCR publish**, and **SWR-G045 (public verification)** kept the
implementation/review loops on a normal contract and **prevented the
unpublished-artifact assumption from recurring**: this slice could not begin until
the selector surfaced issue #199, and the first action it took was to confirm the
tag actually exists on GHCR before doing anything else. The verification trusts the
published bytes (fail-closed inspection), not the metadata or the publish run's
self-reported conclusion — which mattered here, because the publish run was marked
`failure` (build-step timeout) even though the corrected multi-arch, shim-carrying
image had already been pushed. Had publish and verification stayed in one packet, a
reviewer could again have reasoned as if an unpublished tag already existed.

## Related

- [`runtime-host-preview-3-release-metadata.md`](runtime-host-preview-3-release-metadata.md) — the release plan this verifies.
- [`runtime-host-preview-2-release-checklist.md`](runtime-host-preview-2-release-checklist.md) — superseded preview 2 checklist (shim-less, immutable).
- `scripts/release/verify-runtime-host-multiarch.sh` — the fail-closed multi-arch + native-lib gate.
- `src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh` — the public-container end-to-end smoke.
