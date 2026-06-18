# NuGet Preview Release Checklist And Notes Template

Issue: `#137` / `SWR-G012 Preview GitHub Release checklist and release notes
template`

## Summary

The operator-facing NuGet preview release checklist is maintained at
`docs/release/nuget-preview-release-checklist.md`. The GitHub Release notes
template is maintained at `.github/release-notes/nuget-preview.md`.

These artifacts cover the NuGet package release workflow only. They do not
define the later code/repository release checklist.

## Required Operator References

- Checklist: `docs/release/nuget-preview-release-checklist.md`
- Release notes template: `.github/release-notes/nuget-preview.md`
- Release operation: `docs/release/nuget-preview-release.md`
- Version, changelog, and migration-note policy:
  `docs/release/versioning-and-changelog.md`
- Publish gate: `reports/public-release/release-publish-gate.md`
- Durable dry-run evidence:
  `reports/public-release/preview-release-dry-run.md`

## Safe Verification

The checklist requires safe no-publish verification through pull request
validation, optional `workflow_dispatch` dry runs, and the local dry-run command.
It explicitly states that verification must not publish a GitHub Release, run a
local `dotnet nuget push`, or bypass the protected `nuget-preview` environment
approval.

The checklist also states that NuGet release is the first public release
milestone, separate from the later code/repository release checklist, and that a
missing `NUGET_API_KEY` fails a real `release.published` publish attempt instead
of producing a successful skip.
