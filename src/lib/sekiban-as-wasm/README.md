# @sekiban/as-wasm

AssemblyScript projector SDK for the [Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime).

`@sekiban/as-wasm` contains the reusable, domain-agnostic building blocks for
writing Sekiban DCB projector modules in AssemblyScript. Your projector module
keeps the domain logic (state classes, event application, queries, view SQL)
and imports the runtime plumbing from this package:

- **Memory management** — `alloc` / `dealloc` exports the host calls to
  allocate guest memory, plus `readStr` / `writeStr` for the
  `(ptr << 32 | byteLength)` string marshalling convention.
- **Materialized-view protocol** — the `WasmMv*` DTOs the host exchanges with
  `mv_metadata` / `mv_initialize` / `mv_apply_event`, and the
  `statement` / `guidParam` / `stringParam` / `int32Param` /
  `statementBatchPayload` / `errorPayload` / `tableName` / `buildIndexName`
  helpers for building parameterized SQL statement batches.
- **Query helpers** — `applyPaging` for list-query page slicing.

## Install

```bash
npm install @sekiban/as-wasm
npm install --save-dev assemblyscript json-as visitor-as
```

`assemblyscript` (^0.27) and `json-as` (^0.9) are peer dependencies: your
module's `asc` build compiles this package's `assembly/` sources together with
your own, and the JSON DTOs require the `json-as/transform` compiler transform.
`json-as@0.9`'s transform imports `visitor-as`, whose declared peer range stops
at assemblyscript 0.25; resolve that with an npm override in your module:

```json
"overrides": {
  "visitor-as": { "assemblyscript": "$assemblyscript" }
}
```

## Usage

```ts
// assembly/index.ts of your projector module
import { readStr, writeStr } from "@sekiban/as-wasm/assembly";
export { alloc, dealloc } from "@sekiban/as-wasm/assembly";

export function create_instance(namePtr: u32, nameLen: u32): i32 {
  const name = readStr(namePtr, nameLen);
  // ... resolve your projector by name ...
  return 1;
}
```

Materialized-view statement building:

```ts
import {
  WasmMvTableBindingsDto,
  statement,
  guidParam,
  stringParam,
  statementBatchPayload,
  tableName,
} from "@sekiban/as-wasm/assembly";

function insertRow(bindings: WasmMvTableBindingsDto, id: string, sortableUniqueId: string): string {
  const table = tableName(bindings, "rows");
  return statementBatchPayload([
    statement(
      "INSERT INTO " + table + " (id, _last_sortable_unique_id) VALUES (@Id, @SortableUniqueId);",
      [guidParam("Id", id), stringParam("SortableUniqueId", sortableUniqueId)],
    ),
  ]);
}
```

Compile with the transform enabled (CLI flag or `asconfig.json`):

```bash
npx asc assembly/index.ts --outFile modules/my-projector.wasm \
  --optimize --exportStart _initialize --runtime incremental \
  --exportRuntime --use abort= --transform json-as/transform
```

A complete projector built on this package lives in the repository at
`src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/ts-wasm`.

## Compatibility

`@sekiban/as-wasm` 0.1.x targets the Sekiban WASM Runtime host image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and implements
the same guest ABI (alloc/dealloc string marshalling and the `WasmMv*`
materialized-view statement protocol) that the Rust `sekiban-wasm` /
`sekiban-mv` 0.1.0 crates implement.

## License

[Elastic License 2.0](./LICENSE)
