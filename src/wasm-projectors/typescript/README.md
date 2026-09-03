# componentize-js TypeScript guest

This guest keeps the existing AssemblyScript guest intact and adds a separate
componentize-js build for the pinned `sekiban-dcb-ts` meeting-room domain. The
copied `src/domain.ts` is not adapted to compile: its bytes are checked against
`fixtures/domain-pin.json` by `build/scripts/verify-dcb-ts-domain-pin.mjs`.

## Build and verification

From this directory:

```sh
npm ci
npm run test:component
dotnet test ../../internalUsages/cs/SekibanWasm.Cs.Tests/SekibanWasm.Cs.Tests.csproj \
  --filter FullyQualifiedName~TypeScriptComponentGuestTests
```

Normal CI deliberately excludes `TypeScriptComponentGuestTests`, which carry
the xUnit trait `[Trait("Category", "PrivateUpstreamComponent")]`, with
`--filter 'Category!=PrivateUpstreamComponent'`. The component path depends on
the private upstream checkout and the not-yet-public `@sekiban/dcb-domain`
package. This is an explicit test selection, not a pass-on-missing-artifact
fallback: the local commands above remain runnable for authorized developers
and still fail when the component or Preview2 shim is absent. Once the npm
package is public and clean CI can restore the pinned toolchain, remove this
trait/filter and restore the component build steps to normal CI.

`npm run build:component` performs all of the following in scratch space:

- checks the pinned upstream commit, Git blob, byte length, and SHA-256;
- checks `@sekiban/dcb-domain` provenance before choosing an input;
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

## Package provenance finding

The pinned upstream checkout declares `@sekiban/dcb-domain@0.1.0` as private,
with no `publishConfig` and no `.npmrc` registry declaration. The build checked
both registries and found no published artifact:

```text
npm view @sekiban/dcb-domain@0.1.0 version --registry https://registry.npmjs.org --json
=> E404: '@sekiban/dcb-domain@0.1.0' is not in this registry.

npm view @sekiban/dcb-domain@0.1.0 version --registry https://npm.pkg.github.com --json
=> E404: npm package "dcb-domain" does not exist under owner "sekiban".
```

Therefore this run does **not** claim to consume a published ESM artifact. It
uses a clearly labelled fallback: a temporary package `dist/` is built from
the unchanged source at upstream commit
`d9859e71e287c87b872bd9347e117d1b7ee08512`, while the copied consumer domain
remains pinned by SHA-256
`58bcf1aaa141e8f23a5f148b7e8656455ef9a649f8602350794ddb0927fb71dd`.
The generated `build/measurements.json` preserves the complete command output
and fallback provenance.

## First componentize-js evidence

The observed run on 2026-08-25 produced:

| measurement | result | interpretation |
| --- | ---: | --- |
| bundle | 1,138,527 bytes | bundled JS before componentization |
| component | 21,864,350 bytes | componentize-js output |
| componentize-js duration | 2,263.77 ms | build/componentize cost only |
| Preview2 cold start | 614.1996 ms | native Preview2 shim + component instantiation |
| Preview2 per call | 0.01709 ms | 100 calls through the JSON-array P/Invoke bridge |

The Preview2 figures are not componentize-js figures: the existing shim
serializes arguments/results as JSON arrays and crosses P/Invoke. The test
writes the separate `build/preview2-measurements.json` artifact so the two
costs cannot be conflated. This is the first real componentize-js execution
evidence for this domain path and is reported for upstream `sekiban-dcb-ts`
and SekibanCloud as well as this runtime.

The zod `4.4.3` runtime scan covered 210 shipped JavaScript/CJS files. It found
no `eval`, dynamic import, top-level await, or `Date.now`, but did find zod's
`Function` constructor probes/generators, `Math.random` helper, and
Node/global references (including CJS `require`). The emitted bundle retains
those zod `Function` constructors and `Math.random`/`globalThis` references;
componentize-js still completed and the guest executed successfully. These are
dependency findings from the first real run—not edits to the upstream domain—
and are recorded for upstream follow-up.
