#!/usr/bin/env bash
# Tests for NuGet.wasm.config — validates the WASM-specific NuGet configuration
# that includes ILCompiler feeds with packageSourceMapping.
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

# Test 2: All three feeds are present
echo "[Test: package sources]"
assert_contains "nuget.org source" "$CONTENT" 'key="nuget.org"'
assert_contains "dotnet10 source" "$CONTENT" 'key="dotnet10"'
assert_contains "dotnet-experimental source" "$CONTENT" 'key="dotnet-experimental"'
source_count=$(grep -c '<add key=' "$NUGET_WASM_CONFIG" || true)
assert_eq "three package sources defined" "3" "$source_count"

# Test 3: packageSourceMapping section exists
echo "[Test: packageSourceMapping section]"
assert_contains "has packageSourceMapping" "$CONTENT" "<packageSourceMapping>"
assert_contains "has closing tag" "$CONTENT" "</packageSourceMapping>"

# Test 4: nuget.org has wildcard pattern
echo "[Test: nuget.org wildcard]"
assert_contains "nuget.org wildcard pattern" "$CONTENT" 'pattern="*"'

# Test 5: dotnet10 maps ILCompiler packages
echo "[Test: dotnet10 feed mapping]"
assert_contains "dotnet10 ILCompiler pattern" "$CONTENT" 'pattern="Microsoft.DotNet.ILCompiler.*"'
assert_contains "dotnet10 runtime pattern" "$CONTENT" 'pattern="runtime.*.microsoft.dotnet.ilcompiler.*"'

# Test 6: dotnet-experimental maps ILCompiler packages
echo "[Test: dotnet-experimental feed mapping]"
assert_contains "dotnet-experimental source in mapping" "$CONTENT" 'key="dotnet-experimental"'

# Test 7: <clear /> is present to reset inherited sources
echo "[Test: clear directive]"
assert_contains "clear directive present" "$CONTENT" "<clear />"

# Test 8: Valid XML (basic well-formedness check)
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
