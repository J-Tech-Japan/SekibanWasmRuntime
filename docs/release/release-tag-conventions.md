# Release Tag Conventions

Every artifact family releases from its own tag prefix. A published GitHub
Release fans out to every workflow listening on `release: [published]`, so the
prefix — not the workflow — is what decides which lane actually runs.

## The Convention

| Lane | Tag | Workflow | Trigger | Version derived by |
| --- | --- | --- | --- | --- |
| NuGet packages | `v<version>` (e.g. `v1.0.0-preview.2`) | `release-nuget-preview.yml` | release | strip `v` |
| Rust crates (crates.io) | `rust-v<version>` (e.g. `rust-v0.1.1`) | `release-rust-crates.yml` | release | strip `rust-v` |
| npm `@sekiban/*` | `ts-v<version>` | `release-npm-ts.yml` | release | strip `ts-v` |
| Templates package | `templates-v<version>` | `release-templates-preview.yml` | release | strip `templates-v` |
| Runtime host image (GHCR) | `runtime-host-v<version>` | `release-ghcr-image-preview.yml` | tag push | strip `runtime-host-v` |
| Swift SPM | `swift-v<version>` | `release-swift-sdk.yml` | tag push | strip `swift-v` (mirror receives plain `v<version>`) |
| MoonBit packages | `moonbit-v<version>` | `release-moonbit-packages.yml` | tag push | strip `moonbit-v` |
| Go SDK | `src/lib/sekiban-go/v<version>` | `release-go-sdk.yml` | tag push | Go submodule tag format, required by the toolchain |

NuGet is the one lane on a bare `v*` tag, for historical reasons. Every other
lane carries a family prefix, and none of those prefixes begin with `v`.

The **Trigger** column is what decides whether a lane needs a job-level guard.
The `tag push` lanes filter on `push: tags:` in the workflow's `on:` block, so
GitHub only starts them for their own tag pattern and there is nothing to fan
out. The `release` lanes all listen on `release: [published]`, which GitHub
delivers to every one of them regardless of tag — so each must decide for itself
whether the tag is its own. Those are the four lanes carrying a job-level `if`.

## Why Lanes Are Scoped

Before scoping, the NuGet release `v1.0.0-preview.2` started
`release-rust-crates.yml`. Its crate-version consistency check compared
`1.0.0-preview.2` against the 0.1.0 crates and failed
([run 29709790165][run]). Nothing was published — the check did its job — but
the run was red, and the next Rust release would have failed the NuGet lane the
same way. Recurring red runs on correct releases train people to ignore red
runs.

So each release-triggered job carries a job-level `if` on the tag prefix. A
release belonging to another lane **skips**; it does not fail. `workflow_dispatch`
and `pull_request` paths are unaffected.

The NuGet lane scopes itself *positively* on `v*` rather than enumerating the
other prefixes to exclude. Since no other prefix starts with `v`, matching `v`
excludes all of them — including lanes added later, which a hand-maintained
deny-list would silently let through.

## Adding a Lane

1. Pick a prefix that does **not** start with `v` (otherwise it collides with
   the NuGet lane).
2. If the lane triggers on `push: tags:`, the pattern in `on:` is the whole
   guard and there is nothing more to do. If it triggers on
   `release: [published]`, guard every job with a job-level `if`:
   ```yaml
   if: >-
     github.event_name != 'release' ||
     startsWith(github.event.release.tag_name, 'yourlane-v')
   ```
   For publish jobs, combine it with the repository and environment guards.
3. Strip the prefix when deriving the version, keeping any `workflow_dispatch`
   input working as a bare version.
4. Add a row to the table above and a case to
   `scripts/release/check-release-lane-tag-scoping.py`.

## Verification

`scripts/release/check-release-lane-tag-scoping.py` reads the `if:` expressions
out of the committed workflow YAML, evaluates them against synthetic release
payloads, and asserts which jobs run. It does not re-implement the guards, so
drift between a workflow and this document fails the check.

It asserts that the NuGet, Rust, and Templates lanes stay mutually exclusive,
that every other lane prefix starts none of them, that fork releases never
publish, and that `workflow_dispatch` and `pull_request` behavior is unchanged.
The `tag push` lanes are covered too, so converting one of them to a release
trigger without adding a guard fails here rather than in production.

It runs in the `release readiness` job of `release-nuget-preview.yml`, which is
triggered by pull requests touching either release-scoped workflow file.

```
python3 scripts/release/check-release-lane-tag-scoping.py
```

[run]: https://github.com/J-Tech-Japan/SekibanWasmRuntime/actions/runs/29709790165
