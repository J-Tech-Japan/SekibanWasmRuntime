#!/usr/bin/env bash
# SWR-G090 template generation test.
#
# Repeatable pack/install/generate/build validation for the
# sekiban-wasm-decider template:
#   1. Check the explicit template dependency pins against the current release
#      lane and consumer-document defaults.
#   2. dotnet pack Sekiban.Dcb.WasmRuntime.Templates (and the
#      Sekiban.Dcb.WasmRuntime.Aspire dependency, so generated AppHosts can
#      restore it from a local source until the first NuGet publish).
#   3. dotnet new install from the local nupkg in an isolated CLI home.
#   4. Generate with IncludeTests=true and IncludeTests=false under a temp dir
#      using a custom -n name.
#   5. Restore/build the generated Domain, AppHost, and (when present) Tests
#      projects; run the generated tests.
#   6. Verify sourceName substitution left no SekibanDcbDecider residue.
#   7. Uninstall the template package.
#
# The generated Wasm project compiles via the generated scripts/build-wasm.sh
# (NativeAOT-LLVM + WASI SDK) and the live-container smoke needs Docker; both
# are exercised by the sample smokes, not by this generation test.
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"
ROOT="$(pwd)"

PKG_DIR="${TEMPLATE_TEST_PKG_DIR:-$ROOT/artifacts/template-test/packages}"
WORK_DIR="${TEMPLATE_TEST_WORK_DIR:-$ROOT/artifacts/template-test/work}"
CLI_HOME="${TEMPLATE_TEST_CLI_HOME:-$WORK_DIR/dotnet-cli-home}"
TEMPLATE_PROJ="templates/Sekiban.Dcb.WasmRuntime.Templates/Sekiban.Dcb.WasmRuntime.Templates.csproj"
ASPIRE_PROJ="src/lib/Sekiban.Dcb.WasmRuntime.Aspire/Sekiban.Dcb.WasmRuntime.Aspire.csproj"
PACKAGE_ID="Sekiban.Dcb.WasmRuntime.Templates"
CUSTOM_NAME="AcmeWeather"

log() { printf '[template-test] %s\n' "$*"; }
fail() { log "FAIL: $*"; exit 1; }

cleanup() {
  dotnet new uninstall "$PACKAGE_ID" >/dev/null 2>&1 || true
}
trap cleanup EXIT

rm -rf "$PKG_DIR" "$WORK_DIR"
mkdir -p "$PKG_DIR" "$WORK_DIR"
export DOTNET_CLI_HOME="$CLI_HOME"

log "checking template dependency/version drift"
python3 scripts/templates/check-template-version-drift.py \
  || fail "template dependency/version drift check failed"

# The work dir lives under the repo, so stop the repo's Directory.Build.props /
# Directory.Packages.props (central package management) from flowing into the
# generated consumer projects — real consumers build outside this repo.
printf '<Project />\n' > "$WORK_DIR/Directory.Build.props"
printf '<Project />\n' > "$WORK_DIR/Directory.Build.targets"
printf '<Project />\n' > "$WORK_DIR/Directory.Packages.props"

# Optional version override so the templates-v* release lane can validate the
# exact version it would publish (the Aspire dependency keeps the repo version;
# generated AppHosts pin it independently).
TEMPLATE_PACK_ARGS=()
if [[ -n "${TEMPLATE_TEST_PACKAGE_VERSION:-}" ]]; then
  TEMPLATE_PACK_ARGS+=("-p:Version=${TEMPLATE_TEST_PACKAGE_VERSION}")
  log "using template package version override ${TEMPLATE_TEST_PACKAGE_VERSION}"
fi

# Generated AppHosts pin Sekiban.Dcb.WasmRuntime.Aspire to the lane package version
# (currently 1.0.0-preview.7). Until that version is on NuGet, pack it locally at the
# same version the template expects so restore/build does not hit NU1102.
ASPIRE_PACK_VERSION="${TEMPLATE_TEST_ASPIRE_PACKAGE_VERSION:-${RUNTIME_PACKAGE_VERSION:-1.0.0-preview.7}}"

log "packing $PACKAGE_ID and Sekiban.Dcb.WasmRuntime.Aspire@${ASPIRE_PACK_VERSION}"
dotnet pack "$TEMPLATE_PROJ" -c Release -o "$PKG_DIR" --nologo "${TEMPLATE_PACK_ARGS[@]+"${TEMPLATE_PACK_ARGS[@]}"}" >/dev/null || fail "template pack failed"
dotnet pack "$ASPIRE_PROJ" -c Release -o "$PKG_DIR" --nologo "-p:Version=${ASPIRE_PACK_VERSION}" >/dev/null || fail "Aspire package pack failed"

TEMPLATE_NUPKG="$(ls "$PKG_DIR"/$PACKAGE_ID.*.nupkg | head -1)"
[[ -s "$TEMPLATE_NUPKG" ]] || fail "no template nupkg produced"

log "installing template from ${TEMPLATE_NUPKG#$ROOT/}"
dotnet new uninstall "$PACKAGE_ID" >/dev/null 2>&1 || true
dotnet new install "$TEMPLATE_NUPKG" >/dev/null || fail "dotnet new install failed"

# Generated AppHosts reference Sekiban.Dcb.WasmRuntime.Aspire; until the first
# NuGet publish it restores from this local source.
cat > "$WORK_DIR/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-template-test" value="$PKG_DIR" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

generate_and_build() {
  local name="$1" include_tests="$2" out_dir="$WORK_DIR/$1-tests-$2"
  log "generating $name (IncludeTests=$include_tests)"
  mkdir -p "$out_dir"
  (cd "$out_dir" && dotnet new sekiban-wasm-decider -n "$name" --IncludeTests "$include_tests" >/dev/null) \
    || fail "generation failed ($name, IncludeTests=$include_tests)"
  local sol="$out_dir/$name"

  if [[ "$include_tests" == "true" ]]; then
    [[ -d "$sol/$name.Domain.Tests" ]] || fail "Tests project missing with IncludeTests=true"
  else
    [[ ! -d "$sol/$name.Domain.Tests" ]] || fail "Tests project present with IncludeTests=false"
  fi

  cp "$WORK_DIR/NuGet.config" "$sol/NuGet.config"

  log "restoring $name.Domain"
  (cd "$sol" && dotnet restore "$name.Domain/$name.Domain.csproj" --configfile "$sol/NuGet.config" --nologo) \
    || fail "generated Domain restore failed ($name, IncludeTests=$include_tests)"

  log "restoring $name.AppHost (Aspire package from the local source)"
  (cd "$sol" && dotnet restore "$name.AppHost/$name.AppHost.csproj" --configfile "$sol/NuGet.config" --nologo) \
    || fail "generated AppHost restore failed ($name, IncludeTests=$include_tests)"

  if [[ "$include_tests" == "true" ]]; then
    log "restoring $name.Domain.Tests"
    (cd "$sol" && dotnet restore "$name.Domain.Tests/$name.Domain.Tests.csproj" --configfile "$sol/NuGet.config" --nologo) \
      || fail "generated tests restore failed ($name)"
  fi

  log "building $name.Domain"
  (cd "$sol" && dotnet build "$name.Domain/$name.Domain.csproj" -c Release --nologo -v quiet --no-restore) \
    || fail "generated Domain build failed ($name, IncludeTests=$include_tests)"

  log "building $name.AppHost (Aspire package from the local source)"
  (cd "$sol" && dotnet build "$name.AppHost/$name.AppHost.csproj" -c Release --nologo -v quiet --no-restore) \
    || fail "generated AppHost build failed ($name, IncludeTests=$include_tests)"

  if [[ "$include_tests" == "true" ]]; then
    log "building $name.Domain.Tests"
    (cd "$sol" && dotnet build "$name.Domain.Tests/$name.Domain.Tests.csproj" -c Release --nologo -v quiet --no-restore) \
      || fail "generated tests build failed ($name)"
    log "running $name.Domain.Tests"
    (cd "$sol" && dotnet test "$name.Domain.Tests/$name.Domain.Tests.csproj" -c Release --nologo -v quiet --no-build --no-restore) \
      || fail "generated tests failed ($name)"
  else
    # The false shape intentionally has no test project. `dotnet test` on the
    # generated Domain still validates that the test target is a clean no-op
    # after its restore/build, rather than silently skipping the shape.
    log "checking $name.Domain test target (IncludeTests=false has no test project)"
    (cd "$sol" && dotnet test "$name.Domain/$name.Domain.csproj" -c Release --nologo -v quiet --no-build --no-restore) \
      || fail "generated no-tests shape test target failed ($name)"
  fi

  # sourceName substitution must leave no residue anywhere in the output.
  # (grep, not rg: this also runs on stock CI runners.)
  if grep -rn "SekibanDcbDecider" "$sol" --exclude-dir=obj --exclude-dir=bin >/dev/null 2>&1; then
    grep -rn "SekibanDcbDecider" "$sol" --exclude-dir=obj --exclude-dir=bin | head -5 >&2
    fail "residual SekibanDcbDecider strings found in $sol"
  fi
  log "$name (IncludeTests=$include_tests) OK"
}

generate_and_build "$CUSTOM_NAME" true
generate_and_build "$CUSTOM_NAME" false

log "uninstalling $PACKAGE_ID"
dotnet new uninstall "$PACKAGE_ID" >/dev/null || fail "dotnet new uninstall failed"

log "PASS: pack/install/generate/build/residue checks all succeeded"
exit 0
