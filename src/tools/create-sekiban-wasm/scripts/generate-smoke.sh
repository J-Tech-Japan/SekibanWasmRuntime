#!/usr/bin/env bash
# Release-facing generation smoke for create-sekiban-wasm, distinct from the
# package's own `npm test` (test/generate.test.js): this script exercises the
# built CLI exactly as an npx consumer would, for every language, and writes a
# human-readable report to reports/smoke/create-sekiban-wasm-smoke.md
# recording which languages were guard-verified (their bundled
# verify-no-local-sekiban-paths.sh actually ran and passed standalone,
# outside the monorepo) versus tree-verified only (toolchain unavailable, or
# the language's package/tag publish is still pending).
#
# rust and ts must guard-PASS (required by the packet). go/swift/moonbit
# guard failures are logged but do not fail this script, since their
# published packages/tags are still pending (SWR-G058/G061/G063/G065
# follow-ups) -- this mirrors how other per-language smokes in this repo
# report SKIP rather than FAIL when a toolchain or publish artifact isn't
# available yet.
#
# Deliberately avoids bash 4+ features (associative arrays): macOS ships
# bash 3.2 as /bin/bash, and this script must run there too.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

PKG_DIR="src/tools/create-sekiban-wasm"
SMOKE_ROOT="$ROOT/artifacts/create-sekiban-wasm-smoke"
REPORT_DIR="$ROOT/reports/smoke"
REPORT="$REPORT_DIR/create-sekiban-wasm-smoke.md"
RESULTS_TSV="$SMOKE_ROOT/results.tsv"

ALL_LANGUAGES="rust ts go swift moonbit"

log() { printf '[generate-smoke] %s\n' "$*"; }

guard_tool_for() {
  case "$1" in
    rust) echo cargo ;;
    ts) echo node ;;
    go) echo go ;;
    swift) echo swift ;;
    moonbit) echo moon ;;
  esac
}

is_required_language() {
  case "$1" in
    rust|ts) return 0 ;;
    *) return 1 ;;
  esac
}

record_result() {
  # language, result (GUARD-PASS|TREE-ONLY), detail
  printf '%s\t%s\t%s\n' "$1" "$2" "$3" >> "$RESULTS_TSV"
}

write_report() {
  mkdir -p "$REPORT_DIR"
  {
    printf '# create-sekiban-wasm Generation Smoke (SWR-G070)\n\n'
    printf 'Commit: `%s`\n\n' "$(git rev-parse HEAD 2>/dev/null || echo unknown)"
    printf '| Language | Result | Detail |\n'
    printf '| --- | --- | --- |\n'
    for language in $ALL_LANGUAGES; do
      line="$(grep "^$language"$'\t' "$RESULTS_TSV" 2>/dev/null | head -1)"
      if [[ -n "$line" ]]; then
        result="$(printf '%s' "$line" | cut -f2)"
        detail="$(printf '%s' "$line" | cut -f3)"
      else
        result="not-run"
        detail=""
      fi
      printf '| %s | %s | %s |\n' "$language" "$result" "$detail"
    done
    printf '\n`GUARD-PASS` = the language'"'"'s bundled scripts/verify-no-local-sekiban-paths.sh ran standalone in the generated project (outside the monorepo) and passed.\n'
    printf '`TREE-ONLY` = file tree generated and validated; the guard was not run (toolchain unavailable) or did not pass (expected pre-publish for go/swift/moonbit until their package/tag publishes).\n'
  } > "$REPORT"
  log "report: ${REPORT#"$ROOT"/}"
}

fail_hard() { log "FAIL: $*"; write_report; exit 1; }

command -v node >/dev/null 2>&1 || fail_hard "node not found"
command -v npm >/dev/null 2>&1 || fail_hard "npm not found"

if [[ ! -f "$PKG_DIR/dist/cli.js" ]]; then
  log "building CLI (dist/cli.js not found)"
  (cd "$PKG_DIR" && npm install --no-audit --no-fund && npm run sync-templates && npm run build) \
    || fail_hard "package build failed"
fi

CLI="$ROOT/$PKG_DIR/dist/cli.js"
[[ -s "$CLI" ]] || fail_hard "dist/cli.js was not produced"

rm -rf "$SMOKE_ROOT"
mkdir -p "$SMOKE_ROOT"
: > "$RESULTS_TSV"

log "checking unknown --language rejection"
if node "$CLI" --language cobol >/tmp/csw-smoke-unknown.log 2>&1; then
  fail_hard "unknown --language value should have been rejected"
fi
grep -q "Unknown --language value" /tmp/csw-smoke-unknown.log \
  || fail_hard "unknown --language rejection message missing expected text"

log "checking --mode dev reports unavailable rather than generating broken output"
if node "$CLI" --language rust --mode dev --dir "$SMOKE_ROOT/rust-dev" >/tmp/csw-smoke-dev.log 2>&1; then
  fail_hard "--mode dev should be reported as unavailable in 0.1.0"
fi
grep -q "not available" /tmp/csw-smoke-dev.log || fail_hard "dev-mode unavailable message missing expected text"
[[ -e "$SMOKE_ROOT/rust-dev" ]] && fail_hard "--mode dev must not create output when unavailable"

for language in $ALL_LANGUAGES; do
  target_dir="$SMOKE_ROOT/$language"
  log "generating $language (registry mode)"
  if ! node "$CLI" --language "$language" --mode registry --dir "$target_dir" >/tmp/csw-smoke-gen.log 2>&1; then
    fail_hard "generation failed for $language: $(cat /tmp/csw-smoke-gen.log)"
  fi
  [[ -f "$target_dir/README.md" ]] || fail_hard "$language: generated project is missing README.md"
  [[ -f "$target_dir/scripts/verify-no-local-sekiban-paths.sh" ]] \
    || fail_hard "$language: generated project is missing scripts/verify-no-local-sekiban-paths.sh"

  tool="$(guard_tool_for "$language")"
  if ! command -v "$tool" >/dev/null 2>&1; then
    record_result "$language" "TREE-ONLY" "$tool not found in this environment"
    log "$language: TREE-ONLY ($tool not found)"
    continue
  fi

  if (cd "$target_dir" && bash scripts/verify-no-local-sekiban-paths.sh >/tmp/csw-smoke-guard.log 2>&1); then
    record_result "$language" "GUARD-PASS" "bundled guard passed standalone"
    log "$language: GUARD-PASS"
  else
    guard_tail="$(tail -c 300 /tmp/csw-smoke-guard.log | tr '\n' ' ')"
    if is_required_language "$language"; then
      fail_hard "$language: bundled guard must pass standalone (required): $guard_tail"
    fi
    record_result "$language" "TREE-ONLY" "guard did not pass (expected until the package/tag publishes): $guard_tail"
    log "$language: TREE-ONLY (guard did not pass, expected pre-publish)"
  fi
done

log "checking --language all generates every language"
if ! node "$CLI" --language all --dir "$SMOKE_ROOT/all-out" >/tmp/csw-smoke-all.log 2>&1; then
  fail_hard "--language all failed: $(cat /tmp/csw-smoke-all.log)"
fi
for language in $ALL_LANGUAGES; do
  [[ -f "$SMOKE_ROOT/all-out/$language/README.md" ]] \
    || fail_hard "--language all: missing $language subdirectory"
done

write_report
log "PASS"
exit 0
