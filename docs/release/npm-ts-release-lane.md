# npm TypeScript SDK Release Lane (SWR-G058)

The TypeScript SDK packages `@sekiban/ts` (`src/lib/sekiban-ts`) and
`@sekiban/as-wasm` (`src/lib/sekiban-as-wasm`) release together through the
`ts-v*` lane: a published GitHub Release defines the release moment, the tag
defines the package version, and the actual npm publish is gated behind the
protected `npm-release` environment. Package contents and metadata are owned by
`npm-ts-preview-readiness.md` (SWR-G057); this lane only verifies and ships
them.

## Tag Convention

- Release tag: `ts-vX.Y.Z` (first release `ts-v0.1.0`); the version is derived
  by stripping the `ts-v` prefix and must equal the `version` field of **both**
  package.json files.
- `scripts/release/check-npm-package-versions.sh <version>` is the gate (it
  also accepts the full `ts-v0.1.0` tag form) and is runnable locally exactly
  as CI runs it.
- Both packages version together; a release always publishes both.

## Workflow

`.github/workflows/release-npm-ts.yml`:

- **Triggers** — published GitHub Releases (skipped early unless the tag starts
  with `ts-v`, since release events fire repository-wide for every lane) and
  `workflow_dispatch` with `expected_version` plus a `publish` boolean
  defaulting to `false`.
- **verify job (credential-free)** — version gate, `npm ci` + build for both
  packages, `npm pack` tarball inventory, the packed-tarball extraction smoke
  (`scripts/release/npm-extraction-smoke.sh` — tarball-content whitelists,
  no-local-path guard, compiles the samples against the packed tarballs, and a
  public-container load/E2E check that self-reports SKIPPED when Docker is
  unavailable), and `npm publish --dry-run` for both packages. Runs to
  completion with no npm credentials and no `npm-release` environment.
- **publish job** — separate job, `environment: npm-release`, runs only for
  `ts-v*` release events or `publish=true` dispatches. Self-contained on a
  clean runner: it re-runs the version gate and the same `npm ci` + build path
  as verify before publishing (`@sekiban/ts`'s `prepack` needs `tsc` from
  devDependencies). Fails fast with an explicit message while `NPM_TOKEN` is
  absent. Publishes `@sekiban/as-wasm` first, then `@sekiban/ts` (no
  cross-dependency at 0.1.0; the order is convention).

## Dry-Run Procedure (no credentials)

Locally:

```bash
bash scripts/release/check-npm-package-versions.sh 0.1.0
bash scripts/release/npm-extraction-smoke.sh
(cd src/lib/sekiban-ts && npm publish --dry-run --access public)
(cd src/lib/sekiban-as-wasm && npm publish --dry-run --access public)
```

In CI: run the workflow via `workflow_dispatch` with `expected_version` set and
`publish` left `false` — that executes the full verify job and uploads the
packed tarballs plus the smoke report as artifacts.

Note: `npm --prefix <pkg> pack --dry-run` fails on npm 10.9.x with `ENOENT`
(`--prefix` resolves the repo-root package.json before packing); always run
`npm pack` from the package directory, as the workflow and the smoke do.

## Human-Gated Publish Steps

1. Create the `npm-release` protected environment with required reviewers.
2. Provision npm auth for the `@sekiban` scope (granular automation token or
   npm trusted publishing) and store it as the `NPM_TOKEN` secret scoped to
   that environment.
3. Confirm both package.json files are at the target version and the dispatch
   dry-run is green on the release commit.
4. Push the `ts-vX.Y.Z` tag and publish the GitHub Release for it; approve the
   `npm-release` environment gate.
5. After publish, verify `npm view @sekiban/ts@X.Y.Z` and
   `npm view @sekiban/as-wasm@X.Y.Z`, then proceed to the registry-consumer
   proof (SWR-G059).

## Rollback / Republish Notes

- npm versions are immutable: a published version cannot be overwritten.
  `npm unpublish` is restricted (72-hour window, and the version can never be
  reused) — treat it as a last resort for accidental secrets, not as rollback.
- The standard remedy is roll-forward: `npm deprecate @sekiban/ts@X.Y.Z
  "<reason, pointer to fixed version>"` (same for `@sekiban/as-wasm`), bump
  both package.json versions, and cut the next `ts-v*` release.
- If one package publishes and the other fails mid-release, fix the cause and
  re-run the publish job (or an emergency `publish=true` dispatch at the same
  version): `npm publish` of the already-published package fails on the
  duplicate version, so republish only the missing one — deprecate-and-bump
  both if the partial state is user-visible for long.

## Compatibility

`@sekiban/ts` 0.1.x and `@sekiban/as-wasm` 0.1.x pair with runtime image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and the Rust
0.1.0 crates — see `sdk-runtime-compatibility.md` (required docs gate) and
`npm-ts-preview-readiness.md` for the proven evidence.
