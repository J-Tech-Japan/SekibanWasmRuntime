# Migration Notes

This file is the source of truth for public preview migration guidance.

See [`versioning-and-changelog.md`](versioning-and-changelog.md) for the rules
that decide when a migration note is required.
The current preview public API comparison baseline is
[`../../reports/public-release/public-api-semver-baseline.md`](../../reports/public-release/public-api-semver-baseline.md).

## Unreleased

No breaking public contract change beyond the preview.7 entries below.

## 1.0.0-preview.7

### TypeScript: `@sekiban/ts` retired in favor of public DCB packages

- Affected packages / paths: `@sekiban/ts` (removed); TypeScript samples and
  smoke now use `@sekiban/dcb-core`, `@sekiban/dcb-domain`, and
  `@sekiban/dcb-client` `0.2.0`.
- Changed public contract: the unpublished `@sekiban/ts` surface is gone; public
  DCB TypeScript packages own command/read transport and domain typing.
- Required consumer action: replace `@sekiban/ts` imports with the published
  `@sekiban/dcb-*` `0.2.0` packages and `createSekibanExecutor`. Regenerate or
  hand-migrate executor wiring per the updated npm TS samples and
  `src/wasm-projectors/typescript/README.md`.
- Compatibility evidence: SWR-G089 / PR #296 registry-backed smoke and
  transport/read-claim tests.
- Known fallback: none — `@sekiban/ts` was never published to npm.

### Component guests: `apply-event` WIT carries `event-tags`

- Affected packages: component-model WASM guests built against
  `sekiban:wasm/sekiban-projector`; host/runtime packages built from this
  repository at `1.0.0-preview.7`.
- Changed public contract: `apply-event` adds a `list<string> event-tags`
  parameter after `payload-json`. Hosts pass the saved event tag list in
  declaration order; guests must accept and honor it for tag fidelity.
- Required consumer action: rebuild component guests against the updated WIT,
  thread the tag list through `apply-event`, and rerun projector smoke. TypeScript
  guests should follow `src/wasm-projectors/typescript/src/guest.ts` and the
  domain pin at commit `681da42`.
- Compatibility evidence: SWR-G092 / PR #298 `TagProbeProjector` and negative
  controls in `TypeScriptComponentGuestTests`.
- Known fallback: reactor (non-component) guests are unchanged.

## Template For Breaking Changes

Use this shape when a public preview release changes consumer-visible behavior:

```markdown
## 1.0.0-preview.<n>

- Affected packages:
- Changed public contract:
- Required consumer action:
- Compatibility evidence:
- Known fallback:
```
