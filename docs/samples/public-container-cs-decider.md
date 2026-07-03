# Public Container C# Decider Sample

The [`Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider`](../../src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider)
sample is the **public artifact consumption proof**: it runs SekibanWasmRuntime
the way an external developer would — public NuGet packages, the public GHCR
runtime container image, a WASM-compiled Decider domain, and Postgres — with no
repository-local implementation shortcuts.

What it demonstrates:

- A C# Decider domain (`Sekiban.Dcb.WithoutResult`, the `10.2.x` contract line)
  compiled to a `wasi-wasm` module — using only the **public** package.
- An Aspire AppHost that runs the verified public tag
  `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` through the
  `Sekiban.Dcb.WasmRuntime.Aspire` package's single `AddSekibanWasmRuntime`
  call (never `AddProject<...Host>`): the package wires the container image, the
  read-only `.wasm`/manifest bind mounts, the runtime environment contract, and
  the Postgres references (`ConnectionStrings__SekibanDcb` plus a second
  `DcbMaterializedViewPostgres` for the materialized view). See
  [`../nuget/aspire-package-readme.md`](../nuget/aspire-package-readme.md) for
  the package's options surface.
- A smoke that commits an event and reads it back (tag-state + list-query) and
  confirms Materialized View catch-up — all through the running public container.
  See the public-artifact verification evidence in
  [`../release/runtime-host-preview-3-release-verification.md`](../release/runtime-host-preview-3-release-verification.md).

Run it:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
```

See the sample
[`README`](../../src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/README.md)
for the layout, prerequisites, and troubleshooting. This sample must not use
repository-local runtime / `internalUsages` / Sekiban-source references; the
`ProjectReference`s are sample-internal (`Wasm` → `Domain`) plus the
`Sekiban.Dcb.WasmRuntime.Aspire` packable project, which stands in for its NuGet
package until the first publish.
