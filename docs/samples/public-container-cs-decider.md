# Public Container C# Decider Sample

The [`Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider`](../../src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider)
sample is the **public artifact consumption proof**: it runs SekibanWasmRuntime
the way an external developer would — public NuGet packages, the public GHCR
runtime container image, a WASM-compiled Decider domain, and Postgres — with no
repository-local implementation shortcuts.

What it demonstrates:

- A C# Decider domain (`Sekiban.Dcb.WithoutResult`, the `10.2.x` contract line)
  compiled to a `wasi-wasm` module — using only the **public** package.
- An Aspire AppHost that runs
  `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.1` with
  `AddContainer` (never `AddProject<...Host>`), mounts the generated `.wasm` and
  manifest read-only, and wires a Postgres `ConnectionStrings__SekibanDcb`.
- A smoke that commits an event and reads it back (tag-state + list-query)
  through the running public container.

Run it:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
```

See the sample
[`README`](../../src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/README.md)
for the layout, prerequisites, and troubleshooting. This sample must not use
repository-local runtime / `internalUsages` / Sekiban-source references; the only
`ProjectReference` is sample-internal (`Wasm` → `Domain`).
