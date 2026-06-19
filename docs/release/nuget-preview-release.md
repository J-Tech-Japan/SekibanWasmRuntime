# NuGet Preview Release Operation

SekibanWasmRuntime preview packages are published only from a GitHub Release.
Ordinary pushes to `main` and pull request validation do not publish packages.
This NuGet package release is the first public release milestone; the later
code/repository release has a separate checklist and gate in
[`code-repository-release-checklist.md`](code-repository-release-checklist.md).

## Release Inputs

- Use tags in the form `v1.0.0-preview.<n>` or `1.0.0-preview.<n>`.
- The tag version is passed into `dotnet pack` as `PackageVersion`.
- The GitHub Release body is the public release note source.
- `Directory.Build.props` keeps the repository default `VersionPrefix` on the
  active `1.0.0-preview.*` line.
- `CHANGELOG.md` is the human release history source.
- `docs/release/migration-notes.md` is the migration guidance source for
  breaking public contract changes.

See [`versioning-and-changelog.md`](versioning-and-changelog.md) for the full
preview version, changelog, migration-note, and compatibility evidence policy.
Use [`nuget-preview-release-checklist.md`](nuget-preview-release-checklist.md)
for the operator checklist before publishing a GitHub Release. The release notes
body should start from
[`../../.github/release-notes/nuget-preview.md`](../../.github/release-notes/nuget-preview.md).
Use
[`code-repository-release-checklist.md`](code-repository-release-checklist.md)
only for the later source/repository publication stage after NuGet readiness is
complete or explicitly deferred as release-blocking.

## Required Gate

The `release-nuget-preview` workflow runs these checks before any publish step:

- NuGet package inspection for the three public packages.
- Consumer smoke restore/build against the locally packed preview packages.
- High-confidence secret scan.
- License, notice, package README, repository URL, and release evidence checks.
- Public hygiene guardrail.
- Release artifact inventory with package hashes.
- Serialized DCB contract black-box baseline.
- `git diff --check`.

Breaking public contract changes also require a matching migration note and
current compatibility evidence before the release is considered ready.

## Publish Guard

The publish job runs only when all of these are true:

- The event is `release.published`.
- The repository is `J-Tech-Japan/SekibanWasmRuntime`.
- The protected `nuget-preview` environment is approved.
- The publish job has `id-token: write` and can exchange GitHub Actions OIDC
  through `NuGet/login@v1`.
- The NuGet.org Trusted Publishing policy matches package owner `J-Tech-Japan`,
  repository owner `J-Tech-Japan`, repository `SekibanWasmRuntime`, workflow
  file `release-nuget-preview.yml`, and environment `nuget-preview`.

Before publishing, operators must complete the safe environment and policy
preflight recorded in
[`../../reports/public-release/nuget-environment-credential-preflight.md`](../../reports/public-release/nuget-environment-credential-preflight.md).
If GitHub metadata cannot confirm the `nuget-preview` environment, or if an
operator cannot confirm the NuGet.org Trusted Publishing policy, that
uncertainty is release-blocking until both are confirmed.

On a real `release.published` event, the workflow requests a short-lived NuGet
API key through Trusted Publishing immediately before `dotnet nuget push`.
Forks, pull requests, and manual dry runs can run readiness checks, but they
cannot publish.

## Local Dry Run

```bash
PACKAGE_VERSION=1.0.0-preview.1 scripts/release/dry-run-preview-readiness.sh
```

Generated packages and release reports are written under
`artifacts/preview-release-dry-run/`. The durable dry-run evidence pack is
written to `reports/public-release/preview-release-dry-run.md` with explicit
pass, warn, or fail status for package inspection, local-package consumer smoke,
secret scan, license/notice, artifact inventory, serialized DCB contract
evidence, and whitespace validation. The consumer smoke writes generated
evidence to `artifacts/preview-release-dry-run/release/consumer-smoke-local-packages.md`.
The durable public evidence is maintained at
[`../../reports/public-release/consumer-smoke-local-packages.md`](../../reports/public-release/consumer-smoke-local-packages.md)
and references the package selection guidance in
[`../quickstart.md`](../quickstart.md) and
[`../nuget/package-readme.md`](../nuget/package-readme.md). The contract baseline
also writes durable evidence to
`reports/compatibility/serialized-dcb-contract-black-box-baseline.md`.

## Checklist And Notes Template

The NuGet preview release checklist is maintained in
[`nuget-preview-release-checklist.md`](nuget-preview-release-checklist.md). It
covers tag and package version alignment, release notes, required evidence,
safe no-publish verification, protected `nuget-preview` environment approval,
and final publish confirmation.

The GitHub Release notes template is maintained in
[`../../.github/release-notes/nuget-preview.md`](../../.github/release-notes/nuget-preview.md).
Both documents are specific to NuGet package release operations and are not the
later code/repository release checklist. The later checklist is maintained in
[`code-repository-release-checklist.md`](code-repository-release-checklist.md).
