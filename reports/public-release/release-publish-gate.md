# GitHub Release Driven NuGet Publish Gate

Issue: `#125` / `SWR-G006 GitHub Release driven NuGet publish gate`

## Gate Summary

NuGet preview publication is now modeled as a GitHub Release operation rather
than a side effect of ordinary pushes. The release workflow performs readiness
checks for pull requests, manual dry runs, and release events, but the NuGet push
job is reachable only from a published GitHub Release in the canonical
repository.

## Required Readiness Checks

- `scripts/release/inspect-nuget-packages.sh` packs and inspects the three
  public package candidates with a `1.0.0-preview.*` version.
- `scripts/release/check-secrets.sh` runs a high-confidence secret pattern scan
  over tracked repository content, excluding vendored submodules.
- `scripts/release/check-license-notice.sh` verifies required release metadata,
  license, notice, package README, and existing release evidence files.
- `scripts/check-public-hygiene.sh` verifies public repository hygiene.
- `scripts/release/write-artifact-inventory.sh` records package hashes and
  release-relevant tracked assets.
- `git diff --check` verifies whitespace cleanliness.

## Publish Safety

The publish job requires:

- `release.published` event.
- Canonical repository `J-Tech-Japan/SekibanWasmRuntime`.
- Protected `nuget-preview` environment approval.
- Configured `NUGET_API_KEY`.

Missing secrets cause the publish job to exit before `dotnet nuget push`. Pull
requests and forks can validate readiness but do not attempt publish.

