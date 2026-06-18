# Code/Repository Release Staging Review

Date: 2026-06-17
Issue: `#145` / `SWR-G016 Post-NuGet code release staging and license boundary`

## Scope

This review records the source/repository release staging gate that follows the
NuGet preview package readiness gate. It does not publish packages, tag a
source release, or change the license text.

## Reviewed Paths

- `README.md`
- `docs/nuget/package-readme.md`
- `CONTRIBUTING.md`
- `docs/release/nuget-preview-release.md`
- `docs/release/nuget-preview-release-checklist.md`
- `docs/release/versioning-and-changelog.md`
- `docs/release/code-repository-release-checklist.md`

## Findings

- The NuGet preview release remains the first public release milestone.
- The later source/repository publication stage now has a separate checklist in
  `docs/release/code-repository-release-checklist.md`.
- The checklist requires NuGet readiness to pass first, or an explicit
  release-blocking deferral before source publication is staged ahead of a
  package publish.
- README, package README, and CONTRIBUTING consistently state that
  SekibanWasmRuntime may be used, modified, redistributed, and self-hosted,
  including for internal company use.
- The same docs keep the third-party hosted service, managed service, SaaS, or
  similar cloud-provider substitution restriction tied to a separate commercial
  license from J-Tech Japan.
- No reserved cloud-service product branding was introduced.

## Verification

```bash
reserved cloud-service branding search across README, docs, CONTRIBUTING, and reports
git diff --check
```
