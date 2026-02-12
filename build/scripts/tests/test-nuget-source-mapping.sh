#!/usr/bin/env bash
# Tests for NuGet.config — validates that it contains only nuget.org
# (ILCompiler feeds are in NuGet.wasm.config to avoid NU1507).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
NUGET_CONFIG="$ROOT/NuGet.config"
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

assert_not_contains() {
  local test_name="$1" haystack="$2" needle="$3"
  if [[ "$haystack" != *"$needle"* ]]; then
    echo "  PASS: $test_name"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $test_name (expected NOT to contain '$needle')" >&2
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

echo "=== NuGet.config tests (nuget.org only) ==="

CONTENT=$(cat "$NUGET_CONFIG")

# Test 1: nuget.org feed is present
echo "[Test: nuget.org feed]"
assert_contains "nuget.org source" "$CONTENT" 'key="nuget.org"'
assert_contains "nuget.org URL" "$CONTENT" 'https://api.nuget.org/v3/index.json'

# Test 2: No extra feeds (dotnet10, dotnet-experimental must be absent)
echo "[Test: no ILCompiler feeds]"
assert_not_contains "no dotnet10 feed" "$CONTENT" 'key="dotnet10"'
assert_not_contains "no dotnet-experimental feed" "$CONTENT" 'key="dotnet-experimental"'

# Test 3: No packageSourceMapping section (single source does not need it)
echo "[Test: no packageSourceMapping]"
assert_not_contains "no packageSourceMapping" "$CONTENT" "<packageSourceMapping>"

# Test 4: Exactly one package source defined
echo "[Test: single package source]"
source_count=$(grep -c '<add key=' "$NUGET_CONFIG" || true)
assert_eq "one package source defined" "1" "$source_count"

# Test 5: <clear /> is present to reset inherited sources
echo "[Test: clear directive]"
assert_contains "clear directive present" "$CONTENT" "<clear />"

# Test 6: Valid XML (basic well-formedness check)
echo "[Test: XML well-formedness]"
if command -v xmllint &>/dev/null; then
  if xmllint --noout "$NUGET_CONFIG" 2>/dev/null; then
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
