#!/usr/bin/env bash
# Tests for NuGet.wasm.config — validates the WASM-specific NuGet configuration.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
NUGET_WASM_CONFIG="$ROOT/NuGet.wasm.config"
PASS=0
FAIL=0

assert_contains() {
  local test_name="$1" haystack="$2" needle="$3"
  if [[ "$haystack" == *"$needle"* ]]; then
    echo "  PASS: $test_name"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $test_name (expected to contain '$needle')" >&2
    FAIL=$((FAIL + 1))
  fi
}

assert_eq() {
  local test_name="$1" expected="$2" actual="$3"
  if [[ "$expected" == "$actual" ]]; then
    echo "  PASS: $test_name"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $test_name (expected='$expected', actual='$actual')" >&2
    FAIL=$((FAIL + 1))
  fi
}

echo "=== NuGet.wasm.config tests ==="

# Test 1: File exists
echo "[Test: file exists]"
assert_eq "NuGet.wasm.config exists" "true" "$([[ -f "$NUGET_WASM_CONFIG" ]] && echo true || echo false)"

CONTENT=$(cat "$NUGET_WASM_CONFIG")

# Test 2: nuget.org feed is present
echo "[Test: package sources]"
assert_contains "nuget.org source" "$CONTENT" 'key="nuget.org"'
source_count=$(grep -c '<add key=' "$NUGET_WASM_CONFIG" || true)
assert_eq "one package source defined" "1" "$source_count"

# Test 3: no packageSourceMapping section is present
echo "[Test: no packageSourceMapping section]"
if [[ "$CONTENT" == *"<packageSourceMapping>"* ]]; then
  echo "  FAIL: packageSourceMapping should be absent" >&2
  FAIL=$((FAIL + 1))
else
  echo "  PASS: packageSourceMapping is absent"
  PASS=$((PASS + 1))
fi

# Test 4: nuget.org has wildcard pattern
echo "[Test: nuget.org wildcard]"
if [[ "$CONTENT" == *'pattern="*"'* ]]; then
  echo "  FAIL: wildcard mapping should be absent" >&2
  FAIL=$((FAIL + 1))
else
  echo "  PASS: wildcard mapping is absent"
  PASS=$((PASS + 1))
fi

# Test 5: <clear /> is present to reset inherited sources
echo "[Test: clear directive]"
assert_contains "clear directive present" "$CONTENT" "<clear />"

# Test 6: Valid XML (basic well-formedness check)
echo "[Test: XML well-formedness]"
if command -v xmllint &>/dev/null; then
  if xmllint --noout "$NUGET_WASM_CONFIG" 2>/dev/null; then
    echo "  PASS: XML is well-formed"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: XML is not well-formed" >&2
    FAIL=$((FAIL + 1))
  fi
else
  echo "  SKIP: xmllint not available"
fi

# Summary
echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
if [[ $FAIL -gt 0 ]]; then
  exit 1
fi
