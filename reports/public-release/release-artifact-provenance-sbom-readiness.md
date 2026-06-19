# Release Artifact Provenance And SBOM Readiness

Issue: `#163` / `SWR-G025 Release artifact provenance and SBOM readiness`

## Scope

This report records the preview-release decision for artifact provenance and
SBOM expectations. It applies to the initial NuGet preview package release and
does not publish packages, create GitHub Releases, or introduce new
supply-chain infrastructure.

## Current Release Artifact Baseline

The preview release pipeline already records deterministic release evidence for
the package set:

- `scripts/release/write-artifact-inventory.sh` records package filenames,
  package sizes, SHA-256 hashes, and tracked release evidence files.
- `reports/public-release/preview-release-dry-run.md` records the dry-run
  command evidence and links the generated artifact inventory step.
- `reports/public-release/package-inspection.md` records package metadata,
  dependencies, and native content assets for the public package candidates.
- `reports/public-release/license-boundary-commercial-use-signoff.md` records
  the license/commercial-use boundary for the public preview.

This is sufficient provenance evidence for the first NuGet preview because the
release is operator-approved, GitHub Release driven, package hashes are
recorded, and the release does not yet claim SLSA provenance, signed
attestations, or a formal SBOM.

## Decision

| Area | Preview status | Release impact |
| --- | --- | --- |
| Package hash inventory | Implemented | Required before NuGet preview publish. |
| Source/release evidence links | Implemented | Required through the preview release checklist and publish gate. |
| Formal SBOM artifact | Deferred | Non-blocking for the first NuGet preview when this deferral remains explicit. |
| SLSA/in-toto provenance attestation | Deferred | Non-blocking for the first NuGet preview; do not advertise attestation support. |
| Package signing | Deferred | Non-blocking for the first NuGet preview; package hashes remain the integrity evidence. |

## Explicit Deferral

Formal SBOM and provenance attestations are deferred for the first public NuGet
preview. This is a deliberate release decision, not an unreviewed gap.

The preview release must not claim any of the following unless a future PR adds
and verifies the corresponding artifact:

- generated SBOM publication;
- SLSA provenance level;
- in-toto attestations;
- package signing;
- reproducible-build certification.

## Follow-Up Gate

Before any release after `1.0.0-preview.1`, operators must make one of these
decisions and record it in this report or a successor report:

- keep the deferral and state why package hashes plus release evidence remain
  sufficient for that release;
- add a generated SBOM artifact and include it in the release evidence;
- add provenance/attestation generation and include verification output;
- mark the release blocked until the required supply-chain artifact exists.

If a customer, partner, marketplace, or compliance requirement asks for SBOM,
attestation, signing, or reproducibility evidence, that requirement blocks the
affected release until the matching artifact is implemented and verified.

## Operator Checklist

Before publishing the first NuGet preview:

1. Confirm `reports/public-release/preview-release-dry-run.md` is current.
2. Confirm the artifact inventory step has package SHA-256 hashes.
3. Confirm this report still says SBOM/provenance deferral is acceptable for
   the release being published.
4. Confirm GitHub Release notes do not advertise SBOM, SLSA, attestation,
   signing, or reproducible-build support.

## Verification

- This report records the implemented/deferred status and release impact.
- The follow-up gate above makes later SBOM/provenance decisions explicit.
- `scripts/release/check-license-notice.sh` validates that this report exists
  and includes the preview deferral.
- `git diff --check` must pass before merging the release PR.
