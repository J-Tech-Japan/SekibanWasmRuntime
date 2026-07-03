# MoonBit Package Release Lane (SWR-G064)

The MoonBit SDK ships as two mooncakes.io packages from
`src/lib/sekiban-moonbit`:

| Package | Path | Target | Role |
| --- | --- | --- | --- |
| `sekiban/sekiban-wasm-runtime` | `src/lib/sekiban-moonbit/wasm-runtime` | `wasm` | Guest-side projector SDK: linear-memory FFI, string marshalling, C-ABI export plumbing (`create_instance`/`apply_event`/…) driven through `register_callbacks`. |
| `sekiban/sekiban-client` | `src/lib/sekiban-moonbit/client` | `native` | Host-side client SDK: typed commands, tag-state reads, serialized queries over the runtime HTTP contract (uses `moonbitlang/async`). |

Both packages version together (0.1.0 first) and are validated by
`scripts/release/verify-moonbit-package-metadata.sh`, which fails on missing
publish metadata (description/repository/license/keywords/readme), wrong
names or license, missing README/LICENSE files, version drift between the two
packages, or a mismatch against a `moonbit-vX.Y.Z` tag.

## Tag Convention

- Release tag: `moonbit-vX.Y.Z` (first release `moonbit-v0.1.0`).
- The tag version must equal the `version` field of **both** moon.mod.json
  files; the metadata gate enforces this in CI.

## Release Workflow

`.github/workflows/release-moonbit-packages.yml` triggers on `moonbit-v*`
tags plus `workflow_dispatch` (pre-tag dry run):

- **gate** (always): installs the MoonBit toolchain, runs the metadata gate
  (with tag consistency on tag builds), `moon check` + `moon test` for both
  packages, and `moon package` for both — producing the exact publish payload
  zips (`_build/publish/sekiban-sekiban-*-0.1.0.zip`) as the dry-run
  boundary. No credentials are used or required.
- **publish** (tags only): gated behind the `mooncakes-release` protected
  environment. **Currently blocked on human prerequisites** — the step fails
  fast with an explicit message and publishes nothing until they are done.

## Publish Order

`sekiban/sekiban-client` does not currently depend on
`sekiban/sekiban-wasm-runtime` (its deps are `moonbitlang/async` and
`moonbitlang/x`), so there is no dependency-forced order today. Publish
`sekiban/sekiban-wasm-runtime` first, then `sekiban/sekiban-client`, as the
standing convention; if a future version introduces a cross-dependency, the
dependency must be published (and indexed) first.

## Human Prerequisites (blocking the first publish)

1. Register the mooncakes.io account and the `sekiban` scope (`moon register`
   / mooncakes.io signup; the scope must match the `sekiban/` package name
   prefix).
2. Provision publish credentials for CI and store them scoped to the
   `mooncakes-release` protected environment (with required reviewers), then
   replace the workflow's blocked step with the authenticated `moon publish`
   flow for both packages.
3. Push the `moonbit-v0.1.0` tag on the accepted commit and approve the
   environment gate.

After the first publish, verify resolution as a consumer:

```bash
cd "$(mktemp -d)" && moon new verify-sekiban-moonbit && cd verify-sekiban-moonbit
moon add sekiban/sekiban-wasm-runtime
moon add sekiban/sekiban-client
```

## Toolchain Notes (closeout learning)

- Verified with `moon 0.1.20260330`: `moon package` produces
  `_build/publish/<scope>-<name>-<version>.zip` without any registry
  credentials — this is the lane's dry-run packaging step. `moon publish` is
  the only credentialed operation and stays behind the protected environment.
- `moon test` currently reports "no test entry found" (0 tests) for both
  packages; the gate still runs it so tests are enforced as soon as they
  exist. `moon check` passes with warnings and 0 errors.
- mooncakes.io metadata carried in `moon.mod.json`: `description`,
  `repository`, `license` (SPDX `Elastic-2.0`), `keywords`, `readme` — no
  additional required fields surfaced at packaging time.

## Consumer Proof (SWR-G065)

[`src/samples/Sekiban.Dcb.WasmRuntime.Mooncakes.MbDecider`](../../src/samples/Sekiban.Dcb.WasmRuntime.Mooncakes.MbDecider)
is the external-consumer proof for this lane: its committed manifests declare
both sekiban packages as mooncakes.io registry dependencies (guard:
`scripts/verify-no-local-sekiban-paths.sh`), it exercises **both** packages
(projector wasm module from `sekiban/sekiban-wasm-runtime`, typed client from
`sekiban/sekiban-client`), and its smoke validates command execution,
tag-state readback, in-memory projection queries, and materialized-view
catch-up against the public GHCR runtime container.

Two-stage verification:

- **Pre-publish dry-run** (demonstrated now, NOT release evidence):
  `smoke.sh --local-packages` builds a staged copy of the sample under
  `artifacts/` whose manifests are rewritten to path dependencies on
  `src/lib/sekiban-moonbit` — the committed manifests stay registry-only
  (MoonBit has no workspace/mirror overlay, so the staged copy is the
  redirection boundary).
- **Registry-resolved run** (release evidence, after the packages are
  published): `smoke.sh` builds the committed sample from mooncakes.io; this
  is the recorded follow-up once the human account/scope + publish batch
  completes.

Consumer-surfaced fixes (SWR-G065), both in
`src/lib/sekiban-moonbit/client/http/types.mbt`:

- the commit DTO serialized its event bytes under a `payloadBase64` key, but
  the host binds `SerializableEventCandidate` from the camelCase `payload`
  key — commits from the MoonBit client could never succeed; renamed to
  `payload` (plus the `executor.mbt` usage).
- `TagStateResponse` required a `lastSortableUniqueId` field, but the host
  serializes `SerializableTagState` with `lastSortedUniqueId`; the missing
  field failed the whole response parse, so every tag-state read errored;
  renamed to the wire name.

## Compatibility

MoonBit SDK 0.1.x pairs with runtime image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and speaks
the same guest ABI (wasm-runtime) and serialized HTTP contract (client) as
the Rust 0.1.0 crates — see `sdk-runtime-compatibility.md`. The MoonBit
consumer sample against the public container is SWR-G065 (above).
