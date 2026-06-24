# NuGet Preview Release Preparation — Baseline `1.0.0-preview.1`

Issue: `#183` / `SWR-G035 NuGet Preview Release Preparation Kit`

This records the initial baseline preparation for the NuGet preview release. The
preparation kit assembles release artifacts locally; it does **not** publish.
Publishing remains GitHub Release driven, gated by the `nuget-preview`
environment approval and NuGet.org Trusted Publishing (unchanged by this packet).

## Kit

- Command: `PACKAGE_VERSION=1.0.0-preview.1 scripts/release/prepare-nuget-preview-release.sh`
- Source: [`scripts/release/prepare-nuget-preview-release.sh`](../../scripts/release/prepare-nuget-preview-release.sh)
- Flow doc: [`docs/release/nuget-preview-release-preparation.md`](../../docs/release/nuget-preview-release-preparation.md)

## Baseline version

- Version: `1.0.0-preview.1`
- Packages: `Sekiban.Dcb.WasmRuntime`, `Sekiban.Dcb.WasmRuntime.Remote`, `Sekiban.Dcb.WasmRuntime.Wasmtime`

## Generated outputs (git-ignored; regenerate with the command above)

```text
artifacts/release/nuget-preview/1.0.0-preview.1/
  release-body.md        # operator-ready GitHub Release body
  release-checklist.md    # pre-publish gate
  release-summary.md      # what is prepared / what stays manual
  readiness-evidence.md   # link + copy of reports/public-release/preview-release-dry-run.md
```

The generated `release-body.md` is operator-ready (no TODO placeholders) and is
templated from [`.github/release-notes/nuget-preview.md`](../../.github/release-notes/nuget-preview.md).
The kit validates version consistency across body/checklist/summary and the
presence of the package list plus the Trusted Publishing / `nuget-preview`
prerequisites before succeeding.

## Publish-time operator action (unchanged)

Review and publish — not release-text composition: review `release-body.md`,
create the GitHub Release with it, then approve the `nuget-preview` environment
so the Trusted-Publishing publish job runs.
