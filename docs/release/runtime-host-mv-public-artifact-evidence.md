# Public-Artifact Materialized View Sample — Evidence & Blocker (SWR-G043)

The public-container C# Decider sample now demonstrates a **Materialized View (MV)**
through public artifacts: public NuGet for the domain, a WASM module exporting the
MV ABI, and the public GHCR runtime-host image driving MV catch-up into
`DcbMaterializedViewPostgres`.

## What is build-verified (this PR)

- The WASM module exports `mv_metadata`, `mv_initialize`, `mv_apply_event`
  (confirmed present in `public-container-cs-decider.wasm`, ~25.5 MB), alongside
  the existing reactor ABI.
- The generated manifest declares the view:

  ```json
  "materializedViews": [
    { "viewName": "WeatherForecast", "viewVersion": 1,
      "modulePath": "/app/modules/public-container-cs-decider.wasm",
      "logicalTables": ["weather_forecast"] }
  ]
  ```

- The managed builds are clean: the `Wasm` project (MV contracts + projector +
  exports + JSON context) and the `AppHost` (two Postgres databases — `SekibanDcb`
  event store + `DcbMaterializedViewPostgres` — and `SEKIBAN_PROJECTION_MODE=dual`).
- The smoke (`scripts/smoke.sh`) gates `/health` → schema-aware `/ready` →
  identity → commit → tag-state → list-query → **MV read**, where the MV read is
  caller-owned: it resolves the physical table from `sekiban_mv_registry` and polls
  the row directly from `DcbMaterializedViewPostgres`.

## Blocker: the published runtime-host image lacks the preview2 shim

The live MV path (and `list-query`) needs the WASI preview2 shim
(`libwasmtime_preview2_shim.so`), added in **SWR-G042** (PR #194, merged to `main`
at `8381a5a`). The currently published image does **not** carry it:

```text
$ docker run --rm --platform linux/amd64 --entrypoint /bin/sh \
    ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.2 \
    -lc 'ls /app/libwasmtime*.so; echo $WASMTIME_PREVIEW2_SHIM_PATH'
-rw-r--r-- 1 root root 28359032 ... /app/libwasmtime.so      # only the C API
                                                              # (no preview2 shim;
                                                              #  WASMTIME_PREVIEW2_SHIM_PATH empty)
```

The `1.0.0-preview.2` manifest list is multi-arch (`linux/amd64` + `linux/arm64`)
but the image content predates the shim fix (built before `8381a5a`).

**Required release follow-up (container lane), tracked, not hidden:**

- Re-publish the runtime-host image **from `8381a5a` or later** so it carries the
  preview2 shim for both platforms. GHCR run `28142753464` was started from
  `8381a5a`; it must complete and the immutable tag must point at the shim-carrying
  image. Because `1.0.0-preview.2` is immutable, a fixed image may need a new tag
  (e.g. `1.0.0-preview.3`) with the moving `preview` tag advanced to it. Verify
  with `scripts/release/verify-runtime-host-multiarch.sh` (it fails closed on a
  missing per-platform shim) — see
  [`runtime-host-preview-2-release-checklist.md`](runtime-host-preview-2-release-checklist.md).

**Sample readiness is therefore NOT complete.** Per the issue, the sample default
tag is **not** moved to a corrected image until one is published and verified; the
`SAMPLE_RUNTIME_IMAGE_TAG` override lets an operator point the sample at the
corrected tag and run the full smoke through `list-query` + MV:

```bash
SAMPLE_RUNTIME_IMAGE_TAG=<shim-carrying-tag> \
  bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh
SAMPLE_RUNTIME_IMAGE_TAG=<shim-carrying-tag> \
  bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
```

## Public-NuGet support

The domain restores from public NuGet (`Sekiban.Dcb.WithoutResult 10.2.2`); the MV
projector and ABI are sample-internal C# compiled into the WASM module and do not
require any new or changed public package. No NuGet blocker.
