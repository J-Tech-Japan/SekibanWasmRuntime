# @sekiban/aspire

Thin helper for [Aspire](https://aspire.dev) **TypeScript AppHosts** that
registers the public
[Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime)
container with one call: GHCR image, wasm/manifest bind mounts, Postgres
references, the runtime environment contract, and an HTTP endpoint.

If your AppHost is C#, use the `Sekiban.Dcb.WasmRuntime.Aspire` NuGet package
instead — this package is the TypeScript AppHost counterpart, deliberately kept
as a thin function over the official Aspire TS AppHost APIs rather than a port
of the C# options abstraction.

## Install

```bash
npm install @sekiban/aspire
```

Requires an Aspire 13.x TypeScript AppHost (`apphost.mts` created with
`aspire new aspire-ts-empty` or `aspire init`) and Node.js 20+.

## Usage

```ts
// apphost.mts
import { createBuilder } from "./.aspire/modules/aspire.mjs";
import { addSekibanWasmRuntime } from "@sekiban/aspire";

const builder = await createBuilder();

// These database names become ConnectionStrings__SekibanDcb (event store) and
// ConnectionStrings__DcbMaterializedViewPostgres (materialized views) inside
// the runtime container — keep them exactly as shown.
const postgres = builder.addPostgres("pg");
const sekibanDb = postgres.addDatabase("SekibanDcb");
const materializedViewDb = postgres.addDatabase("DcbMaterializedViewPostgres");

await addSekibanWasmRuntime(builder, "runtime", {
  // image defaults to ghcr.io/j-tech-japan/sekiban-wasm-runtime-host,
  // tag to SAMPLE_RUNTIME_IMAGE_TAG or 1.0.0-preview.3.
  configDirectory: "/path/to/config",   // mounted read-only at /app/config
  modulesDirectory: "/path/to/modules", // mounted read-only at /app/modules
  wasmModulePath: "/app/modules/my-projector.wasm",
  references: [sekibanDb, materializedViewDb],
});

await builder.build().run();
```

The helper returns the fluent container resource, so you can keep chaining
official Aspire APIs on the result. It deliberately adds no `waitFor` gate on
the databases: the runtime connects to Postgres lazily and retries.

`env` entries are applied after the standard contract
(`ASPNETCORE_URLS`, `SEKIBAN_PROJECTION_MODE=dual`, `SEKIBAN_MANIFEST_PATH`,
`WASM_MODULE_PATH`), so they win. A fixed `hostPort` publishes the endpoint
unproxied so scripts can reach the runtime deterministically.

A complete AppHost built on this helper lives in the repository:
[PublicContainer.TsAspire sample](https://github.com/J-Tech-Japan/SekibanWasmRuntime/tree/main/src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.TsAspire).
Pair it with [`@sekiban/ts`](https://www.npmjs.com/package/@sekiban/ts) for the
client side.

## Compatibility

`@sekiban/aspire` 0.1.x targets the runtime container image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and the Aspire
13.x TypeScript AppHost generated API surface.

## License

[Elastic License 2.0](./LICENSE)
