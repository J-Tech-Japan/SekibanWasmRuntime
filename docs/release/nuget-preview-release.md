# NuGet Preview Release Operation

SekibanWasmRuntime preview packages are published only from a GitHub Release.
Ordinary pushes to `main` and pull request validation do not publish packages.

## Release Inputs

- Use tags in the form `v1.0.0-preview.<n>` or `1.0.0-preview.<n>`.
- The tag version is passed into `dotnet pack` as `PackageVersion`.
- The GitHub Release body is the public release note source.

## Required Gate

The `release-nuget-preview` workflow runs these checks before any publish step:

- NuGet package inspection for the three public packages.
- High-confidence secret scan.
- License, notice, package README, repository URL, and release evidence checks.
- Public hygiene guardrail.
- Release artifact inventory with package hashes.
- `git diff --check`.

## Publish Guard

The publish job runs only when all of these are true:

- The event is `release.published`.
- The repository is `J-Tech-Japan/SekibanWasmRuntime`.
- The protected `nuget-preview` environment is approved.
- `NUGET_API_KEY` is configured in that environment.

If the secret is missing, the job exits without attempting `dotnet nuget push`.
Forks and pull requests can run readiness checks, but they cannot publish.

## Local Dry Run

```bash
PACKAGE_VERSION=1.0.0-preview.0 scripts/release/inspect-nuget-packages.sh
scripts/release/check-secrets.sh
scripts/release/check-license-notice.sh
scripts/check-public-hygiene.sh
scripts/release/write-artifact-inventory.sh
git diff --check
```

Generated packages and release reports are written under `artifacts/`.

