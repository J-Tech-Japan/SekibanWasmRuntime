#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

report_dir="${RELEASE_REPORT_DIR:-reports/public-release}"
report_path="$report_dir/code-repository-publication-dry-run.md"
mkdir -p "$report_dir"

checks=()
overall="PASS"

set_overall() {
  local status="$1"
  if [[ "$status" == "FAIL" ]]; then
    overall="FAIL"
  elif [[ "$status" == "WARN" && "$overall" == "PASS" ]]; then
    overall="WARN"
  fi
}

record_check() {
  local name="$1"
  local status="$2"
  local evidence="$3"
  checks+=("| $name | $status | $evidence |")
  set_overall "$status"
}

require_file() {
  local path="$1"
  local name="$2"
  if [[ -s "$path" ]]; then
    record_check "$name" "PASS" "\`$path\` exists and is non-empty."
  else
    record_check "$name" "FAIL" "\`$path\` is missing or empty."
  fi
}

require_text() {
  local path="$1"
  local pattern="$2"
  local name="$3"
  local evidence="$4"
  if grep -Eq "$pattern" "$path"; then
    record_check "$name" "PASS" "$evidence"
  else
    record_check "$name" "FAIL" "\`$path\` does not contain the required release wording."
  fi
}

capture_command() {
  local name="$1"
  local command="$2"
  local output_file="$3"
  if bash -c "$command" > "$output_file" 2>&1; then
    record_check "$name" "PASS" "\`$command\` passed."
  else
    record_check "$name" "FAIL" "\`$command\` failed; see generated command output below."
  fi
}

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/swr-code-publication.XXXXXX")"
trap 'rm -rf "$tmp_dir"' EXIT

require_file README.md "README"
require_file CONTRIBUTING.md "CONTRIBUTING"
require_file LICENSE "LICENSE"
require_file NOTICE "NOTICE"
require_file docs/release/code-repository-release-checklist.md "Code release checklist"
require_file reports/public-release/preview-release-dry-run.md "NuGet dry-run evidence"
require_file reports/public-release/nuget-org-post-publish-verification.md "NuGet.org post-publish verification path"

require_text README.md 'Elastic License 2\.0|Elastic License' "README license boundary" "README links the ELv2 license boundary."
require_text CONTRIBUTING.md 'Elastic License 2\.0|Elastic License' "Contribution license boundary" "CONTRIBUTING keeps contributions under ELv2."
require_text NOTICE 'Wasmtime|Sekiban' "NOTICE attribution" "NOTICE includes runtime dependency attribution."
require_text README.md 'hosted service|managed service|SaaS|cloud-provider substitution' "Hosted-service restriction" "README keeps the hosted-service restriction visible."
require_text docs/nuget/package-readme.md 'hosted service|managed service|SaaS|cloud-provider substitution' "Package README hosted-service restriction" "Package README keeps the same hosted-service restriction visible."
require_text docs/release/code-repository-release-checklist.md 'NuGet preview release checklist has passed|NuGet readiness' "NuGet dependency gate" "Source publication checklist keeps the NuGet readiness dependency explicit."
require_text reports/public-release/preview-release-dry-run.md '^PASS with WARN|^PASS' "NuGet readiness dry-run status" "Preview package dry-run evidence records a passing readiness state with documented warnings."

if grep -Eq 'PENDING REAL PUBLISH|FAIL|WARN' reports/public-release/nuget-org-post-publish-verification.md; then
  record_check "NuGet.org publication dependency" "WARN" "NuGet.org post-publish verification is not complete in this source dry-run; do not announce code publication as final until the intended NuGet packages are published and verified."
else
  record_check "NuGet.org publication dependency" "PASS" "NuGet.org post-publish verification does not report a pending or failed state."
fi

reserved_matches="$tmp_dir/reserved-branding.txt"
git grep -nE 'Sekiban(WasmRuntime)? (Cloud|SaaS)|SekibanWasmRuntime Cloud|SekibanWasmRuntime SaaS|managed SekibanWasmRuntime service' \
  -- README.md CONTRIBUTING.md docs reports .github > "$reserved_matches" 2>/dev/null || true
if [[ -s "$reserved_matches" ]]; then
  record_check "Reserved cloud-service branding" "FAIL" "Potential reserved service branding was found; see generated command output below."
else
  record_check "Reserved cloud-service branding" "PASS" "No reserved cloud-service product branding was found in release-facing docs and reports."
fi

capture_command "Public hygiene" "scripts/check-public-hygiene.sh" "$tmp_dir/public-hygiene.txt"
capture_command "Release license/notice check" "scripts/release/check-license-notice.sh" "$tmp_dir/license-notice.txt"
capture_command "Whitespace" "git diff --check" "$tmp_dir/git-diff-check.txt"

{
  printf '# Code Repository Publication Dry Run\n\n'
  printf 'Issue: `#157` / `SWR-G022 Code repository publication dry-run report`\n\n'
  printf '## Scope\n\n'
  printf 'This report records a CI-safe dry run for the later source/repository publication stage. It does not change repository visibility, publish packages, tag a release, or change license text.\n\n'
  printf '## Result\n\n'
  case "$overall" in
    PASS)
      printf 'PASS: source/repository publication checks passed with no warnings.\n\n'
      ;;
    WARN)
      printf 'PASS with WARN: source/repository publication checks passed, but final code publication remains gated by the NuGet readiness dependency called out below.\n\n'
      ;;
    FAIL)
      printf 'FAIL: one or more source/repository publication checks failed.\n\n'
      ;;
  esac
  printf '## NuGet Readiness Dependency\n\n'
  printf 'NuGet release readiness remains the upstream gate for source publication. The local NuGet preview dry-run evidence is present, but `reports/public-release/nuget-org-post-publish-verification.md` still records `PENDING REAL PUBLISH`; therefore final code/repository publication should not be announced as complete until the intended NuGet packages are published and verified from NuGet.org.\n\n'
  printf '## Summary\n\n'
  printf '| Check | Status | Evidence |\n'
  printf '| --- | --- | --- |\n'
  printf '%s\n' "${checks[@]}"
  printf '\n## Commands\n\n'
  printf '```bash\n'
  printf 'scripts/release/dry-run-code-publication.sh\n'
  printf 'scripts/check-public-hygiene.sh\n'
  printf 'scripts/release/check-license-notice.sh\n'
  printf 'git diff --check\n'
  printf '```\n\n'
  printf '## Command Output\n\n'
  printf '### Public hygiene\n\n```text\n'
  sed -e 's/\r$//' "$tmp_dir/public-hygiene.txt"
  printf '```\n\n'
  printf '### Release license/notice check\n\n```text\n'
  sed -e 's/\r$//' "$tmp_dir/license-notice.txt"
  printf '```\n\n'
  printf '### Reserved branding search\n\n```text\n'
  if [[ -s "$reserved_matches" ]]; then
    sed -e 's/\r$//' "$reserved_matches"
  else
    printf 'no reserved cloud-service product branding matches\n'
  fi
  printf '```\n\n'
  printf '### Whitespace\n\n```text\n'
  if [[ -s "$tmp_dir/git-diff-check.txt" ]]; then
    sed -e 's/\r$//' "$tmp_dir/git-diff-check.txt"
  else
    printf 'git diff --check passed\n'
  fi
  printf '```\n\n'
  printf '## Operator Notes\n\n'
  printf -- '- Keep the ELv2 hosted-service restriction visible in README, package README, CONTRIBUTING, and source release notes.\n'
  printf -- '- Treat missing NuGet.org post-publish verification as a release-blocking warning for final source publication.\n'
  printf -- '- Do not change repository visibility from this dry-run path.\n'
} > "$report_path"

cat "$report_path"

if [[ "$overall" == "FAIL" ]]; then
  exit 1
fi
