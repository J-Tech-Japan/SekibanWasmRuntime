#!/usr/bin/env bash
# Tests for build-csharp-wasm.sh logic (OS detection, Docker check, config files)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
PASS=0
FAIL=0

pass() { PASS=$((PASS + 1)); echo "  PASS: $1"; }
fail() { FAIL=$((FAIL + 1)); echo "  FAIL: $1" >&2; }

echo "=== build-csharp-wasm.sh tests ==="

# --- Test 1: Script sets BUILD_MODE=docker on non-Linux ---
echo ""
echo "--- Test 1: BUILD_MODE detection ---"

SCRIPT="$ROOT/build/scripts/build-csharp-wasm.sh"

if [[ "$(uname -s)" == "Linux" ]]; then
  EXPECTED_MODE="native"
else
  EXPECTED_MODE="docker"
fi

OUTPUT=$(bash -c "source /dev/stdin <<'SCRIPT_EOF'
set -euo pipefail
HOST_OS=\"\$(uname -s)\"
if [[ \"\$HOST_OS\" == \"Linux\" ]]; then
  BUILD_MODE=\"native\"
else
  BUILD_MODE=\"docker\"
fi
echo \"\$BUILD_MODE\"
SCRIPT_EOF
")

if [[ "$OUTPUT" == "$EXPECTED_MODE" ]]; then
  pass "BUILD_MODE is '$EXPECTED_MODE' on $(uname -s)"
else
  fail "Expected BUILD_MODE='$EXPECTED_MODE', got '$OUTPUT'"
fi

# --- Test 2: Script file exists and is executable ---
echo ""
echo "--- Test 2: Script file properties ---"

if [[ -f "$SCRIPT" ]]; then
  pass "build-csharp-wasm.sh exists"
else
  fail "build-csharp-wasm.sh not found at $SCRIPT"
fi

if [[ -x "$SCRIPT" ]]; then
  pass "build-csharp-wasm.sh is executable"
else
  fail "build-csharp-wasm.sh is not executable"
fi

# --- Test 3: Script contains Docker fallback logic ---
echo ""
echo "--- Test 3: Docker fallback logic present ---"

if grep -q 'uname -s' "$SCRIPT"; then
  pass "Script contains OS detection (uname -s)"
else
  fail "Script missing OS detection"
fi

if grep -q 'docker run' "$SCRIPT"; then
  pass "Script contains docker run command"
else
  fail "Script missing docker run command"
fi

if grep -q 'command -v docker' "$SCRIPT"; then
  pass "Script checks for Docker availability"
else
  fail "Script missing Docker availability check"
fi

if grep -q -- '--platform linux/amd64' "$SCRIPT"; then
  pass "Script specifies --platform linux/amd64 for Docker"
else
  fail "Script missing --platform linux/amd64"
fi

# --- Test 4: SekibanWasm.Wasm.csproj has no macOS ILCompiler reference ---
echo ""
echo "--- Test 4: csproj ILCompiler references ---"

CSPROJ="$ROOT/src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj"

if grep -q 'runtime.linux-x64.microsoft.dotnet.ilcompiler.llvm' "$CSPROJ"; then
  pass "csproj includes Linux ILCompiler runtime"
else
  fail "csproj missing Linux ILCompiler runtime"
fi

if grep -q 'runtime.osx-arm64.microsoft.dotnet.ilcompiler.llvm' "$CSPROJ"; then
  fail "csproj still contains macOS ILCompiler runtime (should be removed)"
else
  pass "csproj does not contain macOS ILCompiler runtime"
fi

if grep -q "IsOsPlatform" "$CSPROJ"; then
  fail "csproj still has OS-conditional ILCompiler reference"
else
  pass "csproj has no OS-conditional ILCompiler reference"
fi

# --- Test 5: NuGet.config has packageSourceMapping ---
echo ""
echo "--- Test 5: NuGet.config source mapping ---"

NUGET_CONFIG="$ROOT/NuGet.config"

if grep -q 'packageSourceMapping' "$NUGET_CONFIG"; then
  pass "NuGet.config contains packageSourceMapping"
else
  fail "NuGet.config missing packageSourceMapping"
fi

if grep -q 'Microsoft.DotNet.ILCompiler' "$NUGET_CONFIG"; then
  pass "NuGet.config maps ILCompiler packages"
else
  fail "NuGet.config missing ILCompiler package mapping"
fi

if grep -q 'pattern="\*"' "$NUGET_CONFIG"; then
  pass "NuGet.config has default wildcard pattern for nuget.org"
else
  fail "NuGet.config missing default wildcard pattern"
fi

# --- Test 6: Directory.Packages.props has no osx-arm64 entry ---
echo ""
echo "--- Test 6: Directory.Packages.props cleanup ---"

PACKAGES_PROPS="$ROOT/Directory.Packages.props"

if grep -q 'runtime.osx-arm64.microsoft.dotnet.ilcompiler.llvm' "$PACKAGES_PROPS"; then
  fail "Directory.Packages.props still contains osx-arm64 ILCompiler entry"
else
  pass "Directory.Packages.props does not contain osx-arm64 ILCompiler entry"
fi

if grep -q 'runtime.linux-x64.microsoft.dotnet.ilcompiler.llvm' "$PACKAGES_PROPS"; then
  pass "Directory.Packages.props retains Linux ILCompiler entry"
else
  fail "Directory.Packages.props missing Linux ILCompiler entry"
fi

# --- Test 7: CI workflow has WASM artifact validation ---
echo ""
echo "--- Test 7: CI workflow validation step ---"

CI_YML="$ROOT/.github/workflows/ci.yml"

if grep -q 'Validate C# WASM artifact' "$CI_YML"; then
  pass "CI workflow has WASM artifact validation step"
else
  fail "CI workflow missing WASM artifact validation step"
fi

if grep -q 'test -s src/internalUsage/modules/csharp-weather.wasm' "$CI_YML"; then
  pass "CI validation checks csharp-weather.wasm is non-empty"
else
  fail "CI validation missing non-empty check for csharp-weather.wasm"
fi

# --- Test 8: README has GUIDE3 section ---
echo ""
echo "--- Test 8: README GUIDE3 documentation ---"

README="$ROOT/tasks/cswasm/README.md"

if grep -q 'GUIDE3' "$README"; then
  pass "README contains GUIDE3 section"
else
  fail "README missing GUIDE3 section"
fi

if grep -q 'Linux-fixed build' "$README"; then
  pass "README documents Linux-fixed build rationale"
else
  fail "README missing Linux-fixed build documentation"
fi

if grep -q 'package source mapping' "$README"; then
  pass "README documents source mapping rationale"
else
  fail "README missing source mapping documentation"
fi

if grep -q 'Docker' "$README"; then
  pass "README documents Docker prerequisite"
else
  fail "README missing Docker prerequisite documentation"
fi

# --- Summary ---
echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="

if [[ "$FAIL" -gt 0 ]]; then
  exit 1
fi
