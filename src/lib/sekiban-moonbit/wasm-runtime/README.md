# sekiban/sekiban-wasm-runtime

MoonBit projector SDK for the [Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime):
build Sekiban DCB projector modules in MoonBit and compile them to WebAssembly
for the public runtime container.

```bash
moon add sekiban/sekiban-wasm-runtime
```

## Role

This package is the **guest-side (WASM) half** of the MoonBit SDK pair. Your
projector module keeps the domain logic (state types, event application,
queries) and registers it through this package's callback surface; the package
provides the runtime plumbing:

- `ffi` — linear-memory FFI: `alloc`/`dealloc` exports and UTF-8 string
  marshalling with the `(ptr << 32 | byteLength)` packed-`Int64` convention
  the host expects.
- `core` — shared helpers for projector authors: JSON parse/stringify
  helpers, tag-string building, array/paging utilities
  (`apply_paging`, `matches_optional_filter`), and the
  `CommandOutput`/`WasmResult` shapes.
- `projector` — the C-ABI export plumbing (`create_instance`, `apply_event`,
  `apply_event_with_metadata`, `serialize_state`, `restore_state`,
  `execute_query`, `execute_list_query`) wired to your domain via
  `register_callbacks(ProjectorCallbacks)`.

The host-side counterpart is
[`sekiban/sekiban-client`](https://mooncakes.io/docs/sekiban/sekiban-client)
(native target), which talks to the runtime over HTTP.

## Runtime pairing

`sekiban/sekiban-wasm-runtime` 0.1.x targets the public runtime container
image `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and
implements the same guest ABI as the Rust `sekiban-wasm` 0.1.0 crate, the npm
`@sekiban/as-wasm` package, and the Go/Swift SDKs — modules built with any of
these SDKs run side by side on the same runtime image.

A complete projector built on this package lives in the monorepo:
[Sekiban.Dcb.Orleans.Decider.Wasm.Mb sample](https://github.com/J-Tech-Japan/SekibanWasmRuntime/tree/main/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb).

## License

Elastic License 2.0 — see the
[repository LICENSE](https://github.com/J-Tech-Japan/SekibanWasmRuntime/blob/main/LICENSE).
