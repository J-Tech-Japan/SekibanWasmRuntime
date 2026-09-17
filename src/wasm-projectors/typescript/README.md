# componentize-js TypeScript guest

This guest keeps the existing AssemblyScript guest intact and adds a separate
componentize-js build for the pinned `sekiban-dcb-ts` meeting-room domain. The
copied `src/domain.ts` is not adapted to compile: its bytes are checked against
`fixtures/domain-pin.json` by `build/scripts/verify-dcb-ts-domain-pin.mjs`.

## Component apply-event ABI (SWR-G092)

The WIT export carries saved event tags across the component boundary:

```wit
export apply-event: func(
  instance-id: u32,
  event-type: string,
  payload-json: string,
  event-tags: list<string>,
);
```

The export-name set remains the same nine names; only the `apply-event` function
type changed. There is still no `execute-command` export.

`WasmtimeComponentProjectionInstance` passes the exact ordered tag list as the
fourth Preview2 JSON-array bridge argument for `ApplyEvent`, `ApplyEvents`, and
`ApplySerializableEvents`. The TypeScript guest forwards that list unchanged as
`RuntimeProjectionEvent.eventTags`.

Core-module guests are **not** affected by this WIT change. Rust and C#
core-module paths keep their existing tag-aware metadata APIs; run focused
core-module regressions separately when touching the host adapter.

This repository ships one Component Model guest: the componentize-js TypeScript
guest in this directory. There is no other in-repo C# Component Model guest.

Rebuilding the component artifact is required after any WIT or guest change:

```sh
npm run build:component
npm run verify:component
```

Stale component binaries implement the old three-argument `apply-event` type.
The Preview2 JSON-array bridge zips host arguments against the component's
declared parameter list, so extra host-side tags are dropped silently against a
stale export rather than failing at instantiation. The host therefore rejects
components that lack the `event-tags` WIT marker before `ApplyEvent` is called,
and `npm run verify:component` asserts the built artifact exposes a four-argument
`apply-event` export ending with `event-tags`.

## Domain pin provenance

`fixtures/domain-pin.json` records the upstream meeting-room domain tuple:

| field | value |
| --- | --- |
| repository | `J-Tech-Japan/sekiban-dcb-ts` |
| commit | `681da42f3b114f8e5f46422f3eff3e01feb3ccff` (SDT-G88) |
| domainPath | `samples/meeting-room/src/domain.ts` |
| gitBlob | `78b0c67e5f9e208cccdd7a66f807e8c654542d39` |
| sha256 | `2f5c15be3f97ca9e0f355e828541795b9b18087add31f2a139dc81fed4c93705` |
| bytes | `13769` |
| dcbDomainPackage | `@sekiban/dcb-domain@0.2.0` |
| zod | `4.4.3` |

Advance the pin deliberately: update the fixture, re-fetch `src/domain.ts` from
the recorded commit (do not hand-edit the copied upstream file), then verify:

```sh
npm run verify:domain-pin
```

## Tag-fidelity evidence

`src/tag-probe-fixture.ts` defines a local `TagProbeProjector` whose serialized
state records the exact `eventTags` ids observed on apply. This fixture is
local to the guest; it is not part of the pinned upstream domain. It is reachable
via `create-instance("TagProbeProjector")` for tag-fidelity integration tests but
is deliberately omitted from `get-event-types`, which remains the pinned
meeting-room surface only.

- `src/tag-fidelity.test.mjs` — Node controls against `@sekiban/dcb-domain@0.2.0`:
  - distinctive multi-tag input is preserved in state;
  - `eventTags: []` throws `RUNTIME_EVENT_TAGS_EMPTY`;
  - omitted `eventTags` yields synthetic `probe:__runtime__`, unequal to real tags.
- `TypeScriptComponentGuestTests.PinnedTypeScriptComponent_ShouldPreserveDistinctiveEventTagsThroughRealHostPath`
  — integration path through C# host → Preview2 JSON-array bridge → component WIT
  → guest, asserting `observedTagIds` equals
  `["probe:alpha","probe:beta","probe:gamma"]` in order for both `ApplyEvent`
  and `ApplyEvents`.

## Build and verification

From this directory:

```sh
npm ci
npm run verify:domain-pin
npm run test
npm run test:component
dotnet test ../../internalUsages/cs/SekibanWasm.Cs.Tests/SekibanWasm.Cs.Tests.csproj \
  --filter FullyQualifiedName~TypeScriptComponentGuestTests
```

Build the Preview2 shim first when it is absent:

```sh
cargo build --release --manifest-path ../../external/wasmtime-dotnet/native/wasmtime-preview2-shim/Cargo.toml
```

Normal CI deliberately excludes `TypeScriptComponentGuestTests`, which carry
the xUnit trait `[Trait("Category", "PrivateUpstreamComponent")]`, with
`--filter 'Category!=PrivateUpstreamComponent'`. The component path depends on
the pinned upstream checkout and `@sekiban/dcb-domain@0.2.0`. This is an
explicit test selection, not a pass-on-missing-artifact fallback: the local
commands above remain runnable for authorized developers and still fail when the
component or Preview2 shim is absent.

`npm run build:component` performs all of the following in scratch space:

- checks the pinned upstream commit, Git blob, byte length, and SHA-256;
- resolves `@sekiban/dcb-domain@0.2.0` from the public npm registry when available;
- runs the pinned upstream boundary checker without modifying it;
- compiles the consumer with zod `4.4.3` and the locked `jco@1.16.1` /
  `componentize-js@0.19.3` toolchain;
- records a same-domain JavaScript reference result;
- runs `jco componentize` to produce `build/module.wasm`.

The upstream boundary checker is deliberately run unmodified. It hard-codes
`packages/dcb-domain/src` and validates that library source, its negative
fixtures, and the package manifest; it has no consumer-domain target or mode.
Because this consumer `src/domain.ts` imports `@sekiban/dcb-domain`, a checker
`PASS` is not an oracle for the consumer domain file.

`npm run verify:component` validates the component and asserts the exact root
export set:

```text
apply-event, create-instance, deserialize-event, execute-list-query,
execute-query, get-event-types, restore-state, serialize-event, serialize-state
```

There is no command export and no `execute_command` entry. Commands remain
outside this guest; `toRuntimeDomain` may still construct command closures
internally as part of the upstream runtime domain.
