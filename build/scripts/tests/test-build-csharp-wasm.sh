#!/usr/bin/env bash
# Tests for build-csharp-wasm.sh — validates script structure and logic
# without actually running dotnet publish or Docker.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_UNDER_TEST="$SCRIPT_DIR/../build-csharp-wasm.sh"
PASS=0
FAIL=0

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

echo "=== build-csharp-wasm.sh structural tests ==="

# Test 1: Script exists and is executable
echo "[Test: script exists and is executable]"
assert_eq "script exists" "true" "$([[ -f "$SCRIPT_UNDER_TEST" ]] && echo true || echo false)"
assert_eq "script is executable" "true" "$([[ -x "$SCRIPT_UNDER_TEST" ]] && echo true || echo false)"

SCRIPT_CONTENT=$(cat "$SCRIPT_UNDER_TEST")

# Test 2: Script uses set -euo pipefail
echo "[Test: strict mode]"
assert_contains "set -euo pipefail" "$SCRIPT_CONTENT" "set -euo pipefail"

# Test 3: OS detection via uname
echo "[Test: OS detection]"
assert_contains "uname -s for OS detection" "$SCRIPT_CONTENT" 'uname -s'

# Test 4: Build mode selection (native vs docker)
echo "[Test: build mode selection]"
assert_contains "native mode for Linux" "$SCRIPT_CONTENT" 'BUILD_MODE="native"'
assert_contains "docker mode for non-Linux" "$SCRIPT_CONTENT" 'BUILD_MODE="docker"'

# Test 5: Docker image reference matches CI
echo "[Test: Docker image]"
assert_contains "uses dotnet SDK 10.0 preview image" "$SCRIPT_CONTENT" "mcr.microsoft.com/dotnet/sdk:10.0-preview"

# Test 6: WASI SDK version matches CI (v29)
echo "[Test: WASI SDK version]"
assert_contains "wasi-sdk version 29" "$SCRIPT_CONTENT" "wasi_sdk_version=29"

# Test 7: Docker availability check
echo "[Test: Docker availability check]"
assert_contains "checks for docker command" "$SCRIPT_CONTENT" "command -v docker"

# Test 8: Relative paths for Docker container
echo "[Test: relative paths for container]"
assert_contains "relative project path" "$SCRIPT_CONTENT" "WASM_PROJ_REL="
assert_contains "relative publish dir path" "$SCRIPT_CONTENT" "PUBLISH_DIR_REL="

# Test 9: Log output includes host OS and build mode
echo "[Test: log output]"
assert_contains "logs host OS" "$SCRIPT_CONTENT" 'host OS'
assert_contains "logs build mode" "$SCRIPT_CONTENT" 'build mode'

# Test 10: dotnet publish inside Docker uses relative path variables
echo "[Test: in-container dotnet publish uses relative paths]"
# The bash -c block should reference WASM_PROJ_REL and PUBLISH_DIR_REL
assert_contains "WASM_PROJ_REL defined" "$SCRIPT_CONTENT" 'WASM_PROJ_REL='
assert_contains "PUBLISH_DIR_REL defined" "$SCRIPT_CONTENT" 'PUBLISH_DIR_REL='
# The docker bash -c block references the _REL variables for dotnet publish
docker_publish_line=$(grep 'dotnet publish.*WASM_PROJ_REL' "$SCRIPT_UNDER_TEST" || true)
assert_contains "docker publish uses WASM_PROJ_REL" "$docker_publish_line" 'WASM_PROJ_REL'
assert_contains "docker publish uses PUBLISH_DIR_REL" "$docker_publish_line" 'PUBLISH_DIR_REL'

# Test 11: publish_native function uses host paths
echo "[Test: native function uses host paths]"
assert_contains "native function references WASM_PROJ" "$SCRIPT_CONTENT" 'dotnet publish "$WASM_PROJ"'

# Summary
echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
if [[ $FAIL -gt 0 ]]; then
  exit 1
fi
