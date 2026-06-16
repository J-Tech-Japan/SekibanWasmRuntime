#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

fail=0

check_empty() {
  local description="$1"
  local pattern="$2"
  local matches
  matches="$(git ls-files | grep -E "$pattern" || true)"
  if [[ -n "$matches" ]]; then
    printf 'FAIL: %s\n%s\n\n' "$description" "$matches" >&2
    fail=1
  else
    printf 'ok: %s\n' "$description"
  fi
}

check_empty_except() {
  local description="$1"
  local pattern="$2"
  local allowed_pattern="$3"
  local matches
  matches="$(git ls-files | grep -E "$pattern" | grep -Ev "$allowed_pattern" || true)"
  if [[ -n "$matches" ]]; then
    printf 'FAIL: %s\n%s\n\n' "$description" "$matches" >&2
    fail=1
  else
    printf 'ok: %s\n' "$description"
  fi
}

check_required_present() {
  local description="$1"
  shift
  local missing=()
  local path
  for path in "$@"; do
    if ! git ls-files --error-unmatch "$path" >/dev/null 2>&1; then
      missing+=("$path")
    fi
  done

  if (( ${#missing[@]} > 0 )); then
    printf 'FAIL: %s\n' "$description" >&2
    printf '%s\n' "${missing[@]}" >&2
    printf '\n' >&2
    fail=1
  else
    printf 'ok: %s\n' "$description"
  fi
}

check_empty_except \
  'no unclassified tracked host-local automation/editor state' \
  '(^|/)(\.takt|\.intent-cli|\.claude|\.codex|\.vscode|\.idea)(/|$)' \
  '^(\.claude/skills/|\.vscode/mcp\.json$)'
check_empty 'no tracked OS/user-specific files' '(^|/)(\.DS_Store|Thumbs\.db)$|(\.rsuser|\.suo|\.user|\.userosscache|\.sln\.docstates|\.userprefs)$'
check_empty 'no tracked build dependency caches' '(^|/)(bin|obj|node_modules|\.next|\.generated|target|_build|\.mooncakes|BenchmarkDotNet\.Artifacts|TestResults|TestResult|playwright-report)(/|$)'
check_empty 'no tracked generated benchmark logs' '^benchmarks/results/.*\.log$'

check_required_present 'required sample WASM source artifacts remain classified and present' \
  'src/internalUsages/cs/modules/csharp-weather.wasm' \
  'src/internalUsages/rust/modules/rust-weather.wasm' \
  'src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/modules/ts-weather.wasm' \
  'src/samples/Sekiban.Dcb.Orleans.Decider.Wasm/modules/sekiban-dcb-decider.wasm' \
  'src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Go/modules/go-weather.wasm' \
  'src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/modules/sekiban-dcb-decider.wasm' \
  'src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Mb/modules/sekiban-dcb-decider-rust.wasm' \
  'src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/modules/sekiban-dcb-decider.wasm' \
  'src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/modules/sekiban-dcb-decider-rust.wasm' \
  'src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Swift/modules/sekiban-dcb-decider-swift.wasm'

if (( fail != 0 )); then
  exit 1
fi

printf 'public hygiene check passed\n'
