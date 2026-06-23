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
- `scripts/release/consumer-smoke-local-packages.sh` restores and builds a
  generated consumer project against the locally packed preview packages.
- `scripts/release/check-secrets.sh` runs a high-confidence secret pattern scan
  over tracked repository content, excluding vendored submodules.
- `scripts/release/check-license-notice.sh` verifies required release metadata,
  license, notice, package README, and existing release evidence files.
- `scripts/check-public-hygiene.sh` verifies public repository hygiene.
- `scripts/release/write-artifact-inventory.sh` records package hashes and
  release-relevant tracked assets.
- `reports/public-release/release-artifact-provenance-sbom-readiness.md`
  records the preview artifact provenance and SBOM decision. The first NuGet
  preview may proceed with package hashes and release evidence while formal
  SBOM/provenance attestations are explicitly deferred.
- `scripts/contract/run-serialized-dcb-contract-baseline.sh` proves the
  runtime-owned serialized DCB command, query, tag state, and compatibility
  contract baseline before publish.
- `reports/public-release/opentelemetry-vulnerability-triage.md` records the
  OpenTelemetry `NU1902` warning decision. Preview publish is blocked if
  refreshed release evidence still contains unexplained OpenTelemetry
  vulnerability warnings.
- `git diff --check` verifies whitespace cleanliness.

## Publish Safety

The publish job requires:

- `release.published` event.
- Canonical repository `J-Tech-Japan/SekibanWasmRuntime`.
- Protected `nuget-preview` environment approval.
- `id-token: write` permission for the publish job.
- NuGet.org Trusted Publishing policy
  `SekibanWasmRuntime GitHub Release NuGet Preview`.
- `NuGet/login@v1` configured with the NuGet.org policy creator username
  `tomohisa_takaoka`, not the package owner `J-Tech-Japan`.

`reports/public-release/nuget-environment-credential-preflight.md` records the
operator-safe metadata preflight for the protected environment and NuGet.org
Trusted Publishing policy. If the environment or policy cannot be verified,
operators must treat the release as blocked until repository settings confirm
`nuget-preview` and NuGet.org confirms a matching active or temporarily active
policy.

The publish job obtains a short-lived NuGet API key through `NuGet/login@v1`
immediately before `dotnet nuget push`. A failed OIDC exchange fails the real
`release.published` publish job, so a GitHub Release cannot appear successfully
published to NuGet when Trusted Publishing is absent or inactive. Pull requests,
forks, and manual dry runs can validate readiness but do not attempt publish.

Release notes must not claim SBOM publication, SLSA provenance, in-toto
attestations, package signing, or reproducible-build certification unless a
future release PR adds and verifies those artifacts.
