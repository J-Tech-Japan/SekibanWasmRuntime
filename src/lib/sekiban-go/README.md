# sekiban-go

Go SDK for the [Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime).

This module is published directly from the SekibanWasmRuntime monorepo as a
Go subdirectory module — there is no separate repository to clone and no
mirror to sync.

```bash
go get github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go@v0.1.0
```

Releases are cut by pushing tags of the form `src/lib/sekiban-go/vX.Y.Z`
(Go's mandatory convention for subdirectory modules); `@v0.1.0` above resolves
to the repository tag `src/lib/sekiban-go/v0.1.0` through proxy.golang.org.

## Packages

- `client` — `SekibanRuntimeClient`: typed command commit, tag-state reads,
  and serialized query/list-query calls against a running Sekiban WASM
  Runtime host over its HTTP contract.
- `domain` — the typed `Command`/`CommandContext`/`CommandOutput` contract,
  `NewCommandOutput`/`NewMultiEventCommandOutput`, tag helpers, paging, and
  JSON utilities shared by client and projector code.
- `wasm` — guest-side memory management and string marshalling for projector
  modules compiled with TinyGo (`Alloc`/`Dealloc`, `ReadString`/`WriteString`,
  `PackPtrLen`/`UnpackPtrLen`).
- `mv` — the materialized-view SQL statement protocol: `Projector` interface,
  DTOs, parameter builders, and the `Metadata`/`Initialize`/`ApplyEvent`
  export plumbing.

A complete projector + client pair built on this SDK lives in the repository
at `src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Go` (`go-wasm` compiles with
TinyGo to a WASI module; `go-clientapi` drives the runtime over HTTP).

## Runtime pairing

`sekiban-go` 0.1.x targets the public runtime container image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`:

```bash
docker run -p 8080:8080 \
  -v "$PWD/modules:/app/modules:ro" -v "$PWD/config:/app/config:ro" \
  -e SEKIBAN_MANIFEST_PATH=/app/config/sekiban-manifest.json \
  -e WASM_MODULE_PATH=/app/modules/my-projector.wasm \
  -e "ConnectionStrings__SekibanDcb=<postgres connection string>" \
  ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3
```

It speaks the same serialized HTTP contract and guest ABI as the Rust 0.1.0
crates (`sekiban-executor`, `sekiban-wasm`, `sekiban-mv`) and the npm SDKs
(`@sekiban/ts`, `@sekiban/as-wasm`), so modules and clients built with any of
these SDKs interoperate on the same runtime image.

## License

[Elastic License 2.0](./LICENSE)
