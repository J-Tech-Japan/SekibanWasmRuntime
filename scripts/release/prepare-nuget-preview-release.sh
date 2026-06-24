#!/usr/bin/env bash
#
# Local NuGet preview release preparation kit (SWR-G035).
#
# Prepares — but never publishes — the artifacts an operator needs before
# clicking Publish on the GitHub Release that drives the NuGet preview publish:
#   - release-body.md      (operator-ready GitHub Release body for this version)
#   - release-checklist.md (pre-publish checklist for this version)
#   - release-summary.md   (what is prepared / what stays a manual operator step)
#   - readiness-evidence.md (link/copy of the existing dry-run readiness report)
#
# It does NOT pack, push, create a GitHub Release, or touch any credential.
# Publishing remains GitHub Release driven, gated by the `nuget-preview`
# environment approval and NuGet.org Trusted Publishing — unchanged by this kit.
#
# Usage:
#   PACKAGE_VERSION=1.0.0-preview.1 scripts/release/prepare-nuget-preview-release.sh
#   scripts/release/prepare-nuget-preview-release.sh 1.0.0-preview.1
#
# Optional:
#   PREPARE_RUN_DRY_RUN=1   Also run scripts/release/dry-run-preview-readiness.sh
#                           to (re)generate the readiness evidence before copying it.

set -uo pipefail

cd "$(git rev-parse --show-toplevel)" || {
  echo "[prepare] ERROR: not inside a git repository" >&2
  exit 1
}

REPO_ROOT="$(pwd)"
package_version="${PACKAGE_VERSION:-${1:-1.0.0-preview.1}}"
package_version="${package_version#v}"

# Fail closed: only the 1.0.0-preview.* line is supported (matches the dry-run gate).
if [[ ! "$package_version" =~ ^1\.0\.0-preview\.[0-9A-Za-z.-]+$ ]]; then
  printf '[prepare] ERROR: PACKAGE_VERSION must be 1.0.0-preview.*; got %s\n' "$package_version" >&2
  exit 1
fi

baseline_version="1.0.0-preview.1"
template_body=".github/release-notes/nuget-preview.md"
readiness_report="reports/public-release/preview-release-dry-run.md"
out_dir="artifacts/release/nuget-preview/${package_version}"
body_path="$out_dir/release-body.md"
checklist_path="$out_dir/release-checklist.md"
summary_path="$out_dir/release-summary.md"
evidence_path="$out_dir/readiness-evidence.md"

packages=(
  "Sekiban.Dcb.WasmRuntime"
  "Sekiban.Dcb.WasmRuntime.Remote"
  "Sekiban.Dcb.WasmRuntime.Wasmtime"
)

commit="$(git rev-parse HEAD 2>/dev/null || echo unknown)"

[[ -f "$template_body" ]] || {
  printf '[prepare] ERROR: release-notes template not found: %s\n' "$template_body" >&2
  exit 1
}

rm -rf "$out_dir"
mkdir -p "$out_dir"

# --- 1. Optionally (re)generate readiness evidence via the existing dry-run ----

if [[ "${PREPARE_RUN_DRY_RUN:-0}" == "1" ]]; then
  printf '[prepare] running readiness dry-run for %s\n' "$package_version"
  if ! PACKAGE_VERSION="$package_version" bash scripts/release/dry-run-preview-readiness.sh "$package_version"; then
    printf '[prepare] ERROR: readiness dry-run failed; resolve blockers before preparing the release\n' >&2
    exit 1
  fi
fi

# --- 2. release-body.md (version-substituted from the committed template) ------

printf '[prepare] writing %s\n' "$body_path"
sed "s/${baseline_version//./\\.}/${package_version//./\\.}/g" "$template_body" > "$body_path"

# --- 3. readiness-evidence.md (link + copy of the dry-run report) --------------

{
  printf '# Readiness Evidence — %s\n\n' "$package_version"
  printf 'Canonical readiness report: [`%s`](../../../../%s)\n\n' "$readiness_report" "$readiness_report"
  printf 'Regenerate with:\n\n```bash\nPACKAGE_VERSION=%s scripts/release/dry-run-preview-readiness.sh\n```\n\n' "$package_version"
  if [[ -s "$REPO_ROOT/$readiness_report" ]]; then
    printf 'A copy captured at preparation time (commit `%s`) follows.\n\n---\n\n' "$commit"
    cat "$REPO_ROOT/$readiness_report"
  else
    printf '> The readiness report is not present yet. Run the dry-run command above '
    printf '(or re-run this kit with `PREPARE_RUN_DRY_RUN=1`) to generate it.\n'
  fi
} > "$evidence_path"

# --- 4. release-checklist.md --------------------------------------------------

printf '[prepare] writing %s\n' "$checklist_path"
{
  printf '# NuGet Preview Release Checklist — %s\n\n' "$package_version"
  printf 'Prepared from commit `%s`. Publishing is GitHub Release driven; this checklist gates that publish.\n\n' "$commit"
  printf '## Before creating the GitHub Release\n\n'
  printf -- '- [ ] Readiness dry-run passed: `PACKAGE_VERSION=%s scripts/release/dry-run-preview-readiness.sh` (see `readiness-evidence.md`).\n' "$package_version"
  printf -- '- [ ] `Directory.Build.props` `VersionPrefix` matches `%s`.\n' "$package_version"
  printf -- '- [ ] The `nuget-preview` protected GitHub environment exists with required reviewers.\n'
  printf -- '- [ ] NuGet.org Trusted Publishing policy `SekibanWasmRuntime GitHub Release NuGet Preview` is active for `J-Tech-Japan/SekibanWasmRuntime`, workflow `release-nuget-preview.yml`, environment `nuget-preview`.\n'
  printf -- '- [ ] `release-body.md` reviewed; version, package list, and evidence links are correct.\n\n'
  printf '## Create and publish\n\n'
  printf -- '- [ ] Create a GitHub Release tagged for this version using `release-body.md` as the body.\n'
  printf -- '- [ ] Publishing the Release triggers `release-nuget-preview.yml`; approve the `nuget-preview` environment when prompted.\n'
  printf -- '- [ ] Confirm the publish job logged in via NuGet Trusted Publishing (no long-lived API key) and pushed the packages.\n\n'
  printf '## After publish\n\n'
  printf -- '- [ ] Verify the packages on NuGet.org: %s.\n' "$(printf '`%s` ' "${packages[@]}")"
  printf -- '- [ ] Record post-publish verification under `reports/public-release/`.\n'
} > "$checklist_path"

# --- 5. release-summary.md ----------------------------------------------------

printf '[prepare] writing %s\n' "$summary_path"
{
  printf '# NuGet Preview Release Preparation Summary — %s\n\n' "$package_version"
  printf -- '- Version: `%s`\n' "$package_version"
  printf -- '- Prepared from commit: `%s`\n' "$commit"
  printf -- '- Packages: %s\n' "$(printf '`%s` ' "${packages[@]}")"
  printf -- '- Output: `%s`\n\n' "$out_dir"
  printf '## Prepared locally (review these)\n\n'
  printf -- '- `release-body.md` — operator-ready GitHub Release body.\n'
  printf -- '- `release-checklist.md` — pre-publish gate.\n'
  printf -- '- `readiness-evidence.md` — link/copy of the dry-run readiness report.\n\n'
  printf '## Remains a manual operator action\n\n'
  printf 'The publish-time operator action is **review and publish**, not release-text composition:\n\n'
  printf -- '1. Review the generated `release-body.md`.\n'
  printf -- '2. Create the GitHub Release with that body.\n'
  printf -- '3. Approve the `nuget-preview` environment so the Trusted-Publishing publish job runs.\n\n'
  printf '> This kit prepares artifacts only. It does not pack, push, create a GitHub Release, '
  printf 'or alter `release-nuget-preview.yml` publish semantics.\n'
} > "$summary_path"

# --- 6. Validate consistency (fail closed) ------------------------------------

errors=0
for f in "$body_path" "$checklist_path" "$summary_path"; do
  if ! grep -q -- "$package_version" "$f"; then
    printf '[prepare] ERROR: %s does not reference version %s\n' "$f" "$package_version" >&2
    errors=$((errors + 1))
  fi
  if grep -qiE 'TODO|PLACEHOLDER|FIXME|XXXX' "$f"; then
    printf '[prepare] ERROR: %s still contains a placeholder marker\n' "$f" >&2
    errors=$((errors + 1))
  fi
done
for pkg in "${packages[@]}"; do
  if ! grep -q -- "$pkg" "$body_path"; then
    printf '[prepare] ERROR: release-body.md is missing package %s\n' "$pkg" >&2
    errors=$((errors + 1))
  fi
done
for token in "Trusted Publishing" "nuget-preview"; do
  if ! grep -q -- "$token" "$body_path"; then
    printf '[prepare] ERROR: release-body.md is missing required text: %s\n' "$token" >&2
    errors=$((errors + 1))
  fi
done

if (( errors > 0 )); then
  printf '[prepare] FAILED: %s consistency error(s).\n' "$errors" >&2
  exit 1
fi

printf '[prepare] OK: prepared NuGet preview release artifacts for %s under %s\n' "$package_version" "$out_dir"
ls -1 "$out_dir"
