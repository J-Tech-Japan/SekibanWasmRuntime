# NuGet Environment And Trusted Publishing Preflight

Issue: `#151` / `SWR-G019 NuGet environment and credential preflight evidence`

## Scope

This report records the safe preflight for the protected `nuget-preview`
GitHub Environment and the NuGet.org Trusted Publishing policy required by the
NuGet preview publish workflow.

The normal publish path no longer uses a long-lived `NUGET_API_KEY` secret.
Trusted Publishing exchanges GitHub Actions OIDC for a short-lived NuGet API key
immediately before package push. This preflight verifies only metadata when it
is available to the operator.

- Repository: `J-Tech-Japan/SekibanWasmRuntime`
- Workflow: `.github/workflows/release-nuget-preview.yml`
- Trusted Publishing policy: `SekibanWasmRuntime GitHub Release NuGet Preview`
- Package owner: `J-Tech-Japan`
- Workflow environment: `nuget-preview`
- Commit: `5598f4fa7f124ca6d7f13ce3128dcc09e3cf76b1`
- Checked at: `2026-06-19T01:38:19Z`

## Automated Metadata Check

The following command is safe because it requests environment metadata only.
NuGet.org Trusted Publishing policy status is checked manually by an operator
with package-owner access.

```bash
gh api repos/J-Tech-Japan/SekibanWasmRuntime/environments/nuget-preview \
  --jq '{name: .name, protection_rules: (.protection_rules // [] | map(.type)), deployment_branch_policy: .deployment_branch_policy}'
```

Observed result from this implementation checkout:

| Check | Status | Evidence | Release impact |
| --- | --- | --- | --- |
| `nuget-preview` environment metadata | MANUAL BLOCKER | GitHub API returned `404 Not Found`. GitHub may return `404` when the environment does not exist or when the current token cannot read the environment metadata. | Treat as release-blocking until an operator confirms the environment exists in repository settings. |
| NuGet.org Trusted Publishing policy | MANUAL BLOCKER | NuGet.org policy metadata is not available from this checkout. | Treat as release-blocking until an operator confirms the policy exists and is active or temporarily active. |

## Manual Operator Preflight

Before publishing a GitHub Release, an operator with repository administration
access must confirm:

1. Repository Settings -> Environments contains `nuget-preview`.
2. The `nuget-preview` environment has the intended reviewer or protection
   rules for a NuGet preview publish.
3. NuGet.org Trusted Publishing contains policy
   `SekibanWasmRuntime GitHub Release NuGet Preview`.
4. The policy owner is `J-Tech-Japan`, repository owner is `J-Tech-Japan`,
   repository is `SekibanWasmRuntime`, workflow file is
   `release-nuget-preview.yml`, and environment is `nuget-preview`.
5. If the policy is temporarily active, it is still within the 7-day activation
   window for the first successful publish.
6. The release is not published until the dry-run evidence and release notes
   are reviewed and the environment approval is intentionally granted.

## Release-Blocking Rule

Missing or unverified `nuget-preview` environment configuration is
release-blocking.

Missing, inactive, or unverified NuGet.org Trusted Publishing policy
configuration is release-blocking.

The workflow still protects real publishes: a `release.published` run cannot
reach `dotnet nuget push` until `NuGet/login@v1` exchanges GitHub Actions OIDC
for a short-lived NuGet API key, so no credential-free publish is treated as
successful.
