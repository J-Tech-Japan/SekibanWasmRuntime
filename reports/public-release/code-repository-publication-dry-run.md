# Code Repository Publication Dry Run

Issue: `#157` / `SWR-G022 Code repository publication dry-run report`

## Scope

This report records a CI-safe dry run for the later source/repository publication stage. It does not change repository visibility, publish packages, tag a release, or change license text.

## Result

PASS with WARN: source/repository publication checks passed, but final code publication remains gated by the NuGet readiness dependency called out below.

## NuGet Readiness Dependency

NuGet release readiness remains the upstream gate for source publication. The local NuGet preview dry-run evidence is present, but `reports/public-release/nuget-org-post-publish-verification.md` still records `PENDING REAL PUBLISH`; therefore final code/repository publication should not be announced as complete until the intended NuGet packages are published and verified from NuGet.org.

## Summary

| Check | Status | Evidence |
| --- | --- | --- |
| README | PASS | `README.md` exists and is non-empty. |
| CONTRIBUTING | PASS | `CONTRIBUTING.md` exists and is non-empty. |
| LICENSE | PASS | `LICENSE` exists and is non-empty. |
| NOTICE | PASS | `NOTICE` exists and is non-empty. |
| Code release checklist | PASS | `docs/release/code-repository-release-checklist.md` exists and is non-empty. |
| NuGet dry-run evidence | PASS | `reports/public-release/preview-release-dry-run.md` exists and is non-empty. |
| NuGet.org post-publish verification path | PASS | `reports/public-release/nuget-org-post-publish-verification.md` exists and is non-empty. |
| README license boundary | PASS | README links the ELv2 license boundary. |
| Contribution license boundary | PASS | CONTRIBUTING keeps contributions under ELv2. |
| NOTICE attribution | PASS | NOTICE includes runtime dependency attribution. |
| Hosted-service restriction | PASS | README keeps the hosted-service restriction visible. |
| Package README hosted-service restriction | PASS | Package README keeps the same hosted-service restriction visible. |
| NuGet dependency gate | PASS | Source publication checklist keeps the NuGet readiness dependency explicit. |
| NuGet readiness dry-run status | PASS | Preview package dry-run evidence records a passing readiness state with documented warnings. |
| NuGet.org publication dependency | WARN | NuGet.org post-publish verification is not complete in this source dry-run; do not announce code publication as final until the intended NuGet packages are published and verified. |
| Reserved cloud-service branding | PASS | No reserved cloud-service product branding was found in release-facing docs and reports. |
| Public hygiene | PASS | `scripts/check-public-hygiene.sh` passed. |
| Release license/notice check | PASS | `scripts/release/check-license-notice.sh` passed. |
| Whitespace | PASS | `git diff --check` passed. |

## Commands

```bash
scripts/release/dry-run-code-publication.sh
scripts/check-public-hygiene.sh
scripts/release/check-license-notice.sh
git diff --check
```

## Command Output

### Public hygiene

```text
ok: no unclassified tracked host-local automation/editor state
ok: no tracked OS/user-specific files
ok: no tracked build dependency caches
ok: no tracked generated benchmark logs
ok: no tracked generated sample WASM modules
ok: required WASM source and manifest artifacts remain classified and present
public hygiene check passed
```

### Release license/notice check

```text
ok: LICENSE
ok: NOTICE
ok: README.md
ok: docs/nuget/package-readme.md
ok: reports/public-release/readiness-inventory.md
ok: reports/public-release/hygiene-guardrail.md
ok: reports/public-release/wasmtime-preview-inspection.md
ok: reports/public-release/consumer-smoke-local-packages.md
ok: reports/public-release/release-artifact-provenance-sbom-readiness.md
ok: NuGet packages declare LICENSE
ok: NuGet packages declare README
ok: NuGet packages declare repository URL
ok: README license disclosure
ok: NOTICE attribution content
ok: preview SBOM/provenance deferral
license and notice check passed
```

### Reserved branding search

```text
no reserved cloud-service product branding matches
```

### Whitespace

```text
git diff --check passed
```

## Operator Notes

- Keep the ELv2 hosted-service restriction visible in README, package README, CONTRIBUTING, and source release notes.
- Treat missing NuGet.org post-publish verification as a release-blocking warning for final source publication.
- Do not change repository visibility from this dry-run path.
