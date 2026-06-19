# NuGet.org Post-Publish Verification

Issue: `#155` / `SWR-G021 Post-publish NuGet verification checklist`

## Scope

This report defines the operator verification path after a real NuGet preview
publish. It does not publish packages and it is expected to fail before the
target version is visible on NuGet.org.

- Expected package version: `1.0.0-preview.1`
- Package source: `https://api.nuget.org/v3/index.json`
- Verification script: `scripts/release/verify-nuget-org-packages.sh`

## Package Coverage

| Package | Required version |
| --- | --- |
| `Sekiban.Dcb.WasmRuntime` | `1.0.0-preview.1` |
| `Sekiban.Dcb.WasmRuntime.Remote` | `1.0.0-preview.1` |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | `1.0.0-preview.1` |

## Operator Checklist

After the GitHub Release publish workflow reports success:

1. Wait for NuGet.org indexing to make the package pages and restore metadata
   available.
2. Run:

   ```bash
   PACKAGE_VERSION=1.0.0-preview.1 scripts/release/verify-nuget-org-packages.sh
   ```

3. Confirm the generated report states that all three package IDs restored from
   NuGet.org and built in the generated consumer project.
4. Confirm package pages on NuGet.org show `1.0.0-preview.1` for all three
   package IDs before announcing consumer availability.

## Failure Handling

- If restore reports `NU1101` or `NU1102`, wait for NuGet.org indexing and
  retry before announcing availability.
- If only some package IDs restore, treat the release as partially published and
  hold consumer-facing announcements until all three package IDs restore at the
  same version.
- If restore succeeds but build fails, inspect dependency or public API errors
  before marking the release verified.
- Do not publish replacement packages with the same version; NuGet package
  versions are immutable.

## Current Status

PENDING REAL PUBLISH: this verification path is ready, but it should be run only
after the `release.published` workflow successfully pushes the intended preview
packages to NuGet.org.
