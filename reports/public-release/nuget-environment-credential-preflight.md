# NuGet Environment And Credential Preflight

Issue: `#151` / `SWR-G019 NuGet environment and credential preflight evidence`

## Scope

This report records the safe preflight for the protected `nuget-preview`
GitHub Environment and the `NUGET_API_KEY` environment secret required by the
NuGet preview publish workflow.

Secret values must never be printed, copied into logs, or committed. This
preflight verifies only GitHub metadata when it is available to the operator.

- Repository: `J-Tech-Japan/SekibanWasmRuntime`
- Workflow: `.github/workflows/release-nuget-preview.yml`
- Commit: `5598f4fa7f124ca6d7f13ce3128dcc09e3cf76b1`
- Checked at: `2026-06-19T01:38:19Z`

## Automated Metadata Check

The following commands are safe because they request environment metadata and
secret names only. They do not request or expose secret values.

```bash
gh api repos/J-Tech-Japan/SekibanWasmRuntime/environments/nuget-preview \
  --jq '{name: .name, protection_rules: (.protection_rules // [] | map(.type)), deployment_branch_policy: .deployment_branch_policy}'

gh api repos/J-Tech-Japan/SekibanWasmRuntime/environments/nuget-preview/secrets \
  --jq '{total_count: .total_count, secret_names: [.secrets[].name]}'
```

Observed result from this implementation checkout:

| Check | Status | Evidence | Release impact |
| --- | --- | --- | --- |
| `nuget-preview` environment metadata | MANUAL BLOCKER | GitHub API returned `404 Not Found`. GitHub may return `404` when the environment does not exist or when the current token cannot read the environment metadata. | Treat as release-blocking until an operator confirms the environment exists in repository settings. |
| `NUGET_API_KEY` environment secret name metadata | MANUAL BLOCKER | GitHub API returned `404 Not Found` for environment secrets metadata. This preflight did not and must not inspect the secret value. | Treat as release-blocking until an operator confirms a `NUGET_API_KEY` secret exists in the `nuget-preview` environment. |

## Manual Operator Preflight

Before publishing a GitHub Release, an operator with repository administration
access must confirm:

1. Repository Settings -> Environments contains `nuget-preview`.
2. The `nuget-preview` environment has the intended reviewer or protection
   rules for a NuGet preview publish.
3. The `nuget-preview` environment has an environment secret named
   `NUGET_API_KEY`.
4. The secret value is not displayed, copied into a terminal, pasted into an
   issue or PR, or committed.
5. The release is not published until the dry-run evidence and release notes
   are reviewed and the environment approval is intentionally granted.

## Release-Blocking Rule

Missing or unverified `nuget-preview` environment configuration is
release-blocking.

Missing or unverified `NUGET_API_KEY` environment secret configuration is
release-blocking.

The workflow still protects real publishes: a `release.published` run without
`NUGET_API_KEY` fails before `dotnet nuget push`, so no credential-free publish
is treated as successful.

