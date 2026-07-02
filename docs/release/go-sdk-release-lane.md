# Go SDK Release Lane (SWR-G060)

The Go SDK at `src/lib/sekiban-go` is published as a **monorepo subdirectory Go
module** — Go's native mechanism for serving a module from a subdirectory of an
existing public repository. There is no separate Go repository, no mirror, no
sync tooling, and no registry credentials: proxy.golang.org serves tagged
versions straight from the public SekibanWasmRuntime repository.

This retires the earlier mirror-repository plan (a dedicated
`github.com/J-Tech-Japan/sekiban-go` repository kept in sync from the
monorepo). The corrected decision is recorded in the 2026-07-02 grill record;
the retired module path `github.com/J-Tech-Japan/sekiban-go` must not appear
anywhere in the repository, and no sync scripts or sync tokens exist.

## Module Path

Go requires the module path of a subdirectory module to be exactly the
repository path plus the subdirectory:

```
module github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go
```

In-repo consumers (the `go-wasm` and `go-clientapi` samples) import this path
and keep local development working with a `replace` directive pointing at
`../../../lib/sekiban-go`; external consumers just `go get` the module.

## Tag Convention (mandatory)

Go resolves versions of a subdirectory module from tags prefixed with the
subdirectory path. The release tag for version `X.Y.Z` is exactly:

```
src/lib/sekiban-go/vX.Y.Z
```

The first release tag will be `src/lib/sekiban-go/v0.1.0` (all Sekiban SDKs
start at 0.1.0). Plain `vX.Y.Z` or `go-vX.Y.Z` tags do **not** publish this
module and must not be used for it.

Tag and push (done when the lane is accepted, not part of the PR that adds it):

```bash
git tag src/lib/sekiban-go/v0.1.0 <commit>
git push origin src/lib/sekiban-go/v0.1.0
```

## Release Gate Workflow

`.github/workflows/release-go-sdk.yml` triggers on `src/lib/sekiban-go/v*`
tags plus `workflow_dispatch` (pre-tag dry run) and runs, from
`src/lib/sekiban-go`:

1. a tag-format check (`src/lib/sekiban-go/vX.Y.Z`) when running on a tag,
2. a `go.mod` module-path check (repository + subdirectory, exactly),
3. `go build ./...`, `go vet ./...`, `go test ./...`.

Because the tag itself is the publication, the gate needs no secrets, no
protected environment, and has no publish step. Run the `workflow_dispatch`
form before pushing a tag; if the gate fails after a tag was pushed, fix the
issue and cut the next patch version — Go module versions are immutable once
proxy.golang.org has served them.

## Post-Tag GOPROXY Verification

After pushing `src/lib/sekiban-go/v0.1.0`, verify public resolution from a
scratch directory (not inside this repository, so the local `replace`
directives cannot mask a failure):

```bash
cd "$(mktemp -d)"
go mod init verify-sekiban-go
GOPROXY=https://proxy.golang.org go list -m github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go@v0.1.0
GOPROXY=https://proxy.golang.org go get github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go@v0.1.0
```

Expected output of `go list -m`:

```
github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go v0.1.0
```

The first request after tagging may take a few minutes while the proxy fetches
and caches the version. `https://pkg.go.dev/github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go`
appears after the proxy has served the module at least once.

## Compatibility

`sekiban-go` 0.1.x pairs with runtime image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and speaks the
same serialized HTTP contract and guest ABI as the Rust 0.1.0 crates — see
`sdk-runtime-compatibility.md` for the full SDK × runtime matrix.

## Out of Scope for This Lane

- The Go external-consumer sample against the public runtime container is
  SWR-G061.
- No other SDK, crate, or container image is published by this lane.
