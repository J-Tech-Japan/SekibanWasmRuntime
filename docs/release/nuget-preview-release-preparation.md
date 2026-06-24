# NuGet Preview Release Preparation

This describes the **local preparation kit** for NuGet preview releases. The kit
assembles the release wording and version-specific evidence **before** the
operator publishes the GitHub Release. It does not publish anything.

The publish path is unchanged: packages publish **only** from a GitHub Release
publish (`release-nuget-preview.yml`, `release.published`), after `nuget-preview`
environment approval and NuGet.org Trusted Publishing login. See
[`nuget-preview-release.md`](nuget-preview-release.md) and
[`nuget-preview-release-checklist.md`](nuget-preview-release-checklist.md).

## Why

At publish time the operator should **review and publish**, not compose release
text. The kit prepares the version-specific release body, checklist, summary, and
readiness-evidence link so nothing has to be assembled by hand when clicking
Publish.

## Prepare

```bash
PACKAGE_VERSION=1.0.0-preview.1 scripts/release/prepare-nuget-preview-release.sh
```

[`scripts/release/prepare-nuget-preview-release.sh`](../../scripts/release/prepare-nuget-preview-release.sh):

- Accepts `PACKAGE_VERSION` (or the first positional argument) and **fails closed**
  for any version outside the `1.0.0-preview.*` line.
- Writes versioned artifacts to a stable, git-ignored path:

  ```text
  artifacts/release/nuget-preview/<version>/
    release-body.md       # operator-ready GitHub Release body
    release-checklist.md   # pre-publish gate
    release-summary.md     # what is prepared / what stays manual
    readiness-evidence.md  # link + copy of the dry-run readiness report
  ```

- The release body (templated from
  [`.github/release-notes/nuget-preview.md`](../../.github/release-notes/nuget-preview.md))
  contains the package version, package list, highlights, compatibility and
  migration notes, preview limitations, evidence links, the `nuget-preview`
  environment and Trusted Publishing prerequisites, and operator confirmation
  text — with no TODO placeholders.
- Validates that the body, checklist, and summary all reference the same version,
  carry no placeholder markers, and that the body lists all three packages and
  the Trusted Publishing / `nuget-preview` prerequisites. It exits non-zero on
  any inconsistency.

Set `PREPARE_RUN_DRY_RUN=1` to (re)generate the readiness evidence via
[`scripts/release/dry-run-preview-readiness.sh`](../../scripts/release/dry-run-preview-readiness.sh)
before copying it; otherwise the kit links and copies the existing report at
[`reports/public-release/preview-release-dry-run.md`](../../reports/public-release/preview-release-dry-run.md).

The output is git-ignored (`artifacts/`); regenerate it any time by re-running
the command. A committed baseline record for `1.0.0-preview.1` is kept at
[`reports/public-release/nuget-preview-release-preparation.md`](../../reports/public-release/nuget-preview-release-preparation.md).

## Publish (manual operator action — unchanged)

1. Review the generated `release-body.md`.
2. Create the GitHub Release for the version using that body.
3. Approve the `nuget-preview` environment when prompted; the Trusted-Publishing
   publish job pushes the packages.

The kit never packs, pushes, creates a GitHub Release, or changes
`release-nuget-preview.yml` publish semantics.
