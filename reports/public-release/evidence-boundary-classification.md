# Public Release Evidence Boundary Classification

Issue: `#169` / `SWR-G028 Classify public release evidence versus host-only decisions`

## Scope

This report classifies release-facing material that remains in the public
SekibanWasmRuntime repository. The goal is to keep public technical evidence and
release instructions while keeping legal signoff, operator approvals,
commercial strategy, source-publication timing strategy, and product-positioning
decisions in host-side records.

## Public Material Kept

| Category | Public paths | Reason |
| --- | --- | --- |
| Technical package evidence | `reports/public-release/package-inspection.md`, `reports/public-release/consumer-smoke-local-packages.md`, `reports/public-release/wasmtime-preview-inspection.md` | Shows package contents, restore/build behavior, and native asset observations useful to maintainers and consumers. |
| Compatibility evidence | `reports/compatibility/serialized-dcb-contract-black-box-baseline.md`, `reports/compatibility/sekiban-as-a-service-boundary.md` | Documents runtime-owned public contract behavior and downstream integration boundaries. |
| Release instructions | `docs/release/nuget-preview-release.md`, `docs/release/nuget-preview-release-checklist.md`, `.github/release-notes/nuget-preview.md` | Explains how the public NuGet preview is released and what users should know. |
| Public hygiene and readiness checks | `reports/public-release/hygiene-guardrail.md`, `reports/public-release/release-publish-gate.md`, `reports/public-release/preview-release-dry-run.md` | Provides reproducible technical release readiness evidence without exposing host-only approval records. |

## Host-Only Material Excluded

| Category | Public boundary |
| --- | --- |
| Legal/commercial-use signoff evidence | Approval evidence is not stored in this repository. Public docs retain only the concise Elastic License 2.0 allowed-use and hosted-service restriction wording. |
| Operator approval details | Public release docs may state that protected environment and Trusted Publishing confirmation are required, but private approval evidence and repository-setting screenshots belong outside this repo. |
| Commercial or product-positioning strategy | Business timing, source-publication strategy, and cloud-product positioning decisions belong outside this repo unless they are converted into user-facing release notes. |
| Local machine paths | Public reports should not include avoidable `/Users/...` checkout paths. Report generators sanitize the repository root in captured command output to `<repo>`. |

## Public License Boundary

The public README, package README, contribution guide, and release notes keep the
concise license boundary: users may use, modify, redistribute, and self-host
SekibanWasmRuntime, including for internal company use, but third-party hosted
service, managed service, SaaS, or similar cloud-provider substitution requires
a separate commercial license from J-Tech Japan.
