# SekibanWasmRuntime NuGet Preview <version>

Use this template for the GitHub Release body that publishes the NuGet preview
packages. This is the NuGet package release note template, not the later
code/repository release template.

## Packages

- `Sekiban.Dcb.WasmRuntime` `<version>`
- `Sekiban.Dcb.WasmRuntime.Remote` `<version>`
- `Sekiban.Dcb.WasmRuntime.Wasmtime` `<version>`

## Highlights

- <Copy the operator-relevant summary from CHANGELOG.md.>

## Compatibility And Migration

- Migration notes: <link to docs/release/migration-notes.md entry or state that
  no breaking public contract changes are included.>
- Compatibility evidence: <link to current compatibility evidence when
  serialized public contracts changed.>

## Preview Limitations

- <List package selection, runtime deployment, or upgrade limitations that
  matter for preview consumers.>

## Release Evidence

- Dry-run evidence: <link to reports/public-release/preview-release-dry-run.md
  for this package version.>
- Publish gate: <link to reports/public-release/release-publish-gate.md.>
- Checklist: <link to docs/release/nuget-preview-release-checklist.md.>

## Operator Confirmation

- Tag/package version: `<version>`
- Protected environment: `nuget-preview`
- Dry-run command:

  ```bash
  PACKAGE_VERSION=<version> scripts/release/dry-run-preview-readiness.sh
  ```

