#!/usr/bin/env bash
# Tests for SekibanWasm.Wasm.csproj — validates ILCompiler package conditions
# match the GUIDE3 policy (Linux unconditional, macOS opt-in only).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
CSPROJ="$ROOT/src/internalUsages/cs/SekibanWasm.Cs.Wasm/SekibanWasm.Cs.Wasm.csproj"
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

echo "=== SekibanWasm.Wasm.csproj ILCompiler condition tests ==="

CONTENT=$(cat "$CSPROJ")

# Test 1: Linux runtime package is unconditional
echo "[Test: Linux runtime is unconditional]"
# Extract the line with runtime.linux-x64
linux_line=$(grep 'runtime.linux-x64.Microsoft.DotNet.ILCompiler' "$CSPROJ" || true)
assert_contains "linux-x64 package present" "$linux_line" "runtime.linux-x64.Microsoft.DotNet.ILCompiler"
assert_not_contains "linux-x64 has no Condition" "$linux_line" "Condition="

# Test 2: macOS runtime package is opt-in via EnableMacIlCompilerRuntime
echo "[Test: macOS runtime is opt-in]"
# Condition may span multiple lines; use grep -A1 to capture the next line too
osx_block=$(grep -A1 'runtime.osx-arm64.Microsoft.DotNet.ILCompiler' "$CSPROJ" || true)
assert_contains "osx-arm64 package present" "$osx_block" "runtime.osx-arm64.Microsoft.DotNet.ILCompiler"
assert_contains "osx-arm64 uses EnableMacIlCompilerRuntime" "$osx_block" "EnableMacIlCompilerRuntime"

# Test 3: macOS runtime does NOT use IsOsPlatform condition
echo "[Test: no IsOsPlatform for macOS]"
assert_not_contains "no IsOsPlatform('OSX') on osx-arm64" "$osx_block" "IsOsPlatform"

# Test 4: Linux runtime does NOT use IsOsPlatform condition
echo "[Test: no IsOsPlatform for Linux]"
assert_not_contains "no IsOsPlatform('Linux') on linux-x64 line" "$linux_line" "IsOsPlatform"

# Test 5: Meta-package is always included
echo "[Test: ILCompiler meta-package present]"
assert_contains "Microsoft.DotNet.ILCompiler present" "$CONTENT" 'Include="Microsoft.DotNet.ILCompiler"'

# Summary
echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
if [[ $FAIL -gt 0 ]]; then
  exit 1
fi
