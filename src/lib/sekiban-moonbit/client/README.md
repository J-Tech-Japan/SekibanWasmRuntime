# sekiban/sekiban-client

MoonBit host-side client SDK for the [Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime):
drive a running Sekiban runtime container — typed commands, tag-state reads,
and serialized queries — from a native MoonBit application.

```bash
moon add sekiban/sekiban-client
```

## Role

This package is the **host-side (native) half** of the MoonBit SDK pair; the
guest-side projector SDK is
[`sekiban/sekiban-wasm-runtime`](https://mooncakes.io/docs/sekiban/sekiban-wasm-runtime)
(wasm target). It targets MoonBit's native backend and uses
`moonbitlang/async` for the HTTP client:

- `http` — typed request/response DTOs for the runtime's serialized HTTP
  contract (`TagStateRequest`/`TagStateResponse`, `CommitRequest` with event
  candidates and consistency tags, `QueryRequest`, …) and the HTTP client.
- `executor` — the command execution flow (read consistency tag-state,
  run the command, commit event candidates) plus
  `StaticTagProjectorResolver` for mapping tag groups to the projectors
  declared in the runtime manifest.
- `server` — routing/handler helpers for exposing your own API in front of
  the runtime.

## Runtime pairing

`sekiban/sekiban-client` 0.1.x targets the public runtime container image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`:

```bash
docker run -p 8080:8080 \
  -v "$PWD/modules:/app/modules:ro" -v "$PWD/config:/app/config:ro" \
  -e SEKIBAN_MANIFEST_PATH=/app/config/sekiban-manifest.json \
  -e WASM_MODULE_PATH=/app/modules/my-projector.wasm \
  -e "ConnectionStrings__SekibanDcb=<postgres connection string>" \
  ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3
```

It speaks the same serialized HTTP contract as the Rust `sekiban-executor`
0.1.0 crate and the npm `@sekiban/ts` package, so clients built with any of
these SDKs interoperate with the same runtime image and modules.

A complete client built on this package lives in the monorepo:
[Sekiban.Dcb.Orleans.Decider.Wasm.Mb sample](https://github.com/J-Tech-Japan/SekibanWasmRuntime/tree/main/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb).

## License

Elastic License 2.0 — see the
[repository LICENSE](https://github.com/J-Tech-Japan/SekibanWasmRuntime/blob/main/LICENSE).
