# License Boundary And Commercial-Use Signoff

Issue: `#159` / `SWR-G023 License boundary and commercial-use signoff checklist`

## Scope

This report records the public-facing license-boundary review for NuGet preview
and later source/repository publication. It does not change the `LICENSE` text,
introduce new legal terms, or grant any commercial exception.

## Result

PASS WITH RELEASE BLOCKER: the reviewed public-facing docs consistently keep the
Elastic License 2.0 hosted-service restriction visible, but final publication
requires operator/legal signoff that the wording is acceptable as written.

## Reviewed Public Surfaces

| Surface | Status | Evidence |
| --- | --- | --- |
| Repository README | PASS | `README.md` states that SekibanWasmRuntime may be used, modified, redistributed, and self-hosted, including for internal company use, and that third-party hosted service, managed service, SaaS, or similar offering requires a separate commercial license from J-Tech Japan. |
| Package README | PASS | `docs/nuget/package-readme.md` repeats the same allowed-use and hosted-service restriction for preview package consumers. |
| Contribution guide | PASS | `CONTRIBUTING.md` states that contributions are licensed under Elastic License 2.0 and repeats the same license boundary for contributed work. |
| NuGet release notes | PASS | `.github/release-notes/nuget-preview.md` states that the Elastic License 2.0 hosted-service restriction remains in force and that the NuGet preview does not grant hosted, managed, SaaS, or similar third-party service rights without a separate commercial license. |
| NuGet release checklist | PASS | `docs/release/nuget-preview-release-checklist.md` now requires this signoff report before package publication. |
| Source/repository release checklist | PASS | `docs/release/code-repository-release-checklist.md` now requires this signoff report before code/repository publication. |
| Source/repository dry run | PASS WITH WARN | `reports/public-release/code-repository-publication-dry-run.md` keeps the ELv2 hosted-service restriction visible and warns that final publication still depends on upstream NuGet verification. |

## Boundary Statements Confirmed

- Allowed use is described as use, modification, redistribution, and
  self-hosting, including internal company use.
- The restricted use is described as providing SekibanWasmRuntime to third
  parties as a hosted service, managed service, SaaS, or similar offering that
  gives users access to a substantial set of its features, unless a separate
  commercial license has been agreed with J-Tech Japan.
- Upstream Sekiban remains described as Apache License 2.0 and separate from
  this repository's Elastic License 2.0 boundary.
- No reserved cloud-service branding or cloud-service product promise is
  introduced by the reviewed surfaces.

## Release-Blocking Signoff

The wording is consistent across the reviewed surfaces, but this report is not a
legal approval. Before publishing NuGet packages or announcing source/repository
publication, an operator must confirm that the current wording is approved for
public release.

Treat any of the following as release-blocking until resolved:

- legal review requests different wording for the hosted-service restriction;
- package README, release notes, or source publication notes drift from the
  README boundary;
- a release note implies third-party hosted-service or managed-service rights
  without a separate commercial license;
- reserved cloud-service branding is introduced before the product owner
  explicitly approves that branding.

## Verification

- Link/path review covered `README.md`, `CONTRIBUTING.md`,
  `docs/nuget/package-readme.md`, `.github/release-notes/nuget-preview.md`,
  `docs/release/nuget-preview-release-checklist.md`, and
  `docs/release/code-repository-release-checklist.md`.
- `git diff --check` must pass before merging the release PR.
