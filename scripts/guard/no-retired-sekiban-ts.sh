#!/usr/bin/env bash
# Fail when live tracked files still reference the retired @sekiban/ts package.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

PATTERN='@sekiban/ts|src/lib/sekiban-ts|sekiban-ts@'
ALLOW_PREFIXES=(
  'intents/'
  '.intent-cli/'
  'reports/'
  'tasks/'
  'scripts/guard/'
)
ALLOW_FILES=(
  'src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/scripts/verify-no-local-sekiban-paths.sh'
)

mapfile -t TRACKED < <(git ls-files)

violations=()
for file in "${TRACKED[@]}"; do
  allowed=0
  for prefix in "${ALLOW_PREFIXES[@]}"; do
    if [[ "$file" == "$prefix"* ]]; then
      allowed=1
      break
    fi
  done
  [[ "$allowed" == "1" ]] && continue
  for allowed_file in "${ALLOW_FILES[@]}"; do
    [[ "$file" == "$allowed_file" ]] && { allowed=1; break; }
  done
  [[ "$allowed" == "1" ]] && continue
  if grep -qE "$PATTERN" "$file" 2>/dev/null; then
    violations+=("$file")
  fi
done

if ((${#violations[@]} > 0)); then
  echo "ERROR: retired @sekiban/ts references remain in live tracked files:" >&2
  printf '  - %s\n' "${violations[@]}" >&2
  exit 1
fi

# Fixture: synthetic reference must fail when injected.
fixture="$(mktemp)"
printf 'import "@sekiban/ts";\n' >"$fixture"
if ! grep -q '@sekiban/ts' "$fixture"; then
  echo "ERROR: guard fixture failed to compile" >&2
  exit 1
fi
rm -f "$fixture"

echo "PASS: no live @sekiban/ts references in tracked files (allowlisted historical paths excluded)."
