# Release Tag Conventions

Every artifact family releases from its own tag prefix. A published GitHub
Release fans out to every workflow listening on `release: [published]`, so the
prefix — not the workflow — is what decides which lane actually runs.

## The Convention

| Lane | Tag | Workflow | Version derived by |
| --- | --- | --- | --- |
| NuGet packages | `v<version>` (e.g. `v1.0.0-preview.2`) | `release-nuget-preview.yml` | strip `v` |
| Rust crates (crates.io) | `rust-v<version>` (e.g. `rust-v0.1.1`) | `release-rust-crates.yml` | strip `rust-v` |
| Runtime host image (GHCR) | `runtime-host-v<version>` | `release-ghcr-image-preview.yml` | strip `runtime-host-v` |
| npm `@sekiban/*` | `ts-v<version>` | `release-npm-ts.yml` | strip `ts-v` |
| Swift SPM | `swift-v<version>` | `release-swift-sdk.yml` | strip `swift-v` (mirror receives plain `v<version>`) |
| MoonBit packages | `moonbit-v<version>` | `release-moonbit-packages.yml` | strip `moonbit-v` |
| Go SDK | `src/lib/sekiban-go/v<version>` | `release-go-sdk.yml` | Go submodule tag format, required by the toolchain |

NuGet is the one lane on a bare `v*` tag, for historical reasons. Every other
lane carries a family prefix, and none of those prefixes begin with `v`.

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
2. Guard every release-triggered job with a job-level `if`:
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
drift between a workflow and this document fails the check. It runs in the
`release readiness` job of `release-nuget-preview.yml`, which is triggered by
pull requests touching either lane's workflow file.

```
python3 scripts/release/check-release-lane-tag-scoping.py
```

[run]: https://github.com/J-Tech-Japan/SekibanWasmRuntime/actions/runs/29709790165
