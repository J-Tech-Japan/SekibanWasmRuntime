# Preview Versioning, Changelog, and Migration Notes

This file is the release operator source of truth for SekibanWasmRuntime preview
version, changelog, and migration-note discipline.

## Source Of Truth

| Release input | Source of truth | Required update |
| --- | --- | --- |
| Current preview version line | `Directory.Build.props` `VersionPrefix` | Keep on the active `1.0.0-preview.*` line. |
| Published package version | GitHub Release tag, normalized by `.github/workflows/release-nuget-preview.yml` | Use `v1.0.0-preview.<n>` or `1.0.0-preview.<n>`. |
| Human changelog | `CHANGELOG.md` | Add an `Unreleased` entry before every public preview release. |
| Public release notes | GitHub Release body | Summarize the matching `CHANGELOG.md` entry and link migration notes when required. |
| Migration guidance | `docs/release/migration-notes.md` | Add an entry for every breaking public contract change. |
| Public API baseline | `reports/public-release/public-api-semver-baseline.md` | Refresh when public package types, serialized DTOs, package metadata, or dependency shape changes. |
| Compatibility evidence | `reports/compatibility/serialized-dcb-contract-black-box-baseline.md` | Refresh when serialized public contracts change. |
| Artifact provenance and SBOM decision | `reports/public-release/release-artifact-provenance-sbom-readiness.md` | Confirm the implemented/deferred status before every public preview release. |
| Later source/repository release staging | `docs/release/code-repository-release-checklist.md` | Run only after NuGet readiness passes or a release-blocking deferral is recorded. |

`Directory.Build.props` supplies the repository default `VersionPrefix` for the
three public packages. The release workflow still passes the GitHub Release tag
into `dotnet pack` as `PackageVersion`, so a release tag is the final published
package version for that release.

## Preview Version Rules

- Initial public preview packages stay on the `1.0.0-preview.*` line.
- Increment the preview suffix for each published preview release.
- Do not change package identity as part of version housekeeping.
- Keep all public packages on the same preview version unless a release plan
  explicitly documents why one package is intentionally skipped.
- Do not publish from ordinary pushes or pull request validation; publishing is
  only allowed through the protected GitHub Release workflow.

## Changelog Rules

Before a public preview release, update `CHANGELOG.md` with:

- New public package surface or package metadata changes.
- Runtime behavior changes visible to package consumers.
- Compatibility evidence added, refreshed, or invalidated.
- Migration-note links for breaking public contract changes.
- Known preview limitations that affect package selection or upgrade safety.

The GitHub Release body is the external release note. It should copy the
operator-relevant summary from `CHANGELOG.md`, not introduce a separate release
history.

## Breaking Public Contract Changes

A change is treated as a breaking public contract change when it can require a
package consumer, sample consumer, or downstream hosted integration to change
code, configuration, serialized payload handling, package selection, or runtime
deployment behavior.

Breaking public contract changes require all of the following before release:

- A `CHANGELOG.md` entry that names the breaking change.
- A `docs/release/migration-notes.md` entry with affected packages, migration
  steps, and compatibility impact.
- A refreshed `reports/public-release/public-api-semver-baseline.md` entry or
  linked API-diff evidence when public package surface changes.
- Compatibility evidence from the serialized DCB contract baseline, or an
  explicit release-blocking note explaining why evidence is not available.
- GitHub Release notes that link the migration entry.

When the compatibility contract changes, refresh
`reports/compatibility/serialized-dcb-contract-black-box-baseline.md` before the
release PR is considered ready.

## Release Operator Checklist

1. Confirm `Directory.Build.props` remains on the expected preview line.
2. Confirm the GitHub Release tag uses `v1.0.0-preview.<n>` or
   `1.0.0-preview.<n>`.
3. Confirm `CHANGELOG.md` has the release entry.
4. Confirm `docs/release/migration-notes.md` has entries for every breaking
   public contract change, or explicitly says none are required for the release.
5. Confirm `reports/public-release/public-api-semver-baseline.md` is current
   when public package surfaces changed.
6. Confirm compatibility evidence is current when serialized contracts changed.
7. Confirm the artifact provenance and SBOM readiness report still matches the
   release being published, and that release notes do not advertise deferred
   supply-chain artifacts.
8. Run the preview readiness dry run and keep the resulting evidence pack.
9. For later source/repository publication, run
   `docs/release/code-repository-release-checklist.md` after NuGet readiness is
   complete or explicitly deferred as release-blocking.
10. Run `git diff --check`.
