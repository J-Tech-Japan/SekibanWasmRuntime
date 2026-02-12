#!/usr/bin/env bash
# Tests for NuGet.config packageSourceMapping — validates the XML structure
# ensures all required feeds and patterns are present.
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

echo "=== NuGet.config source mapping tests ==="

CONTENT=$(cat "$NUGET_CONFIG")

# Test 1: packageSourceMapping section exists
echo "[Test: packageSourceMapping section]"
assert_contains "has packageSourceMapping" "$CONTENT" "<packageSourceMapping>"
assert_contains "has closing tag" "$CONTENT" "</packageSourceMapping>"

# Test 2: nuget.org is default with wildcard
echo "[Test: nuget.org default source]"
assert_contains "nuget.org source key" "$CONTENT" 'key="nuget.org"'
assert_contains "nuget.org wildcard pattern" "$CONTENT" 'pattern="*"'

# Test 3: dotnet10 feed maps ILCompiler packages
echo "[Test: dotnet10 feed mapping]"
assert_contains "dotnet10 source key" "$CONTENT" 'key="dotnet10"'
assert_contains "dotnet10 ILCompiler pattern" "$CONTENT" 'pattern="Microsoft.DotNet.ILCompiler.*"'
assert_contains "dotnet10 runtime pattern" "$CONTENT" 'pattern="runtime.*.microsoft.dotnet.ilcompiler.*"'

# Test 4: dotnet-experimental feed maps ILCompiler packages
echo "[Test: dotnet-experimental feed mapping]"
assert_contains "dotnet-experimental source key" "$CONTENT" 'key="dotnet-experimental"'

# Test 5: Valid XML (basic well-formedness check)
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

# Test 6: Three package sources defined
echo "[Test: package sources]"
source_count=$(grep -c '<add key=' "$NUGET_CONFIG" || true)
assert_eq "three package sources defined" "3" "$source_count"

# Summary
echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
if [[ $FAIL -gt 0 ]]; then
  exit 1
fi
