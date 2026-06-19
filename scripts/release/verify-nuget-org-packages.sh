#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

package_version="${PACKAGE_VERSION:-${1:-1.0.0-preview.1}}"
package_version="${package_version#v}"

if [[ ! "$package_version" =~ ^1\.0\.0-preview\.[0-9A-Za-z.-]+$ ]]; then
  printf 'Package version must be 1.0.0-preview.*; got %s\n' "$package_version" >&2
  exit 1
fi

verify_root="${NUGET_ORG_VERIFY_DIR:-artifacts/nuget-org-verify}"
report_dir="${RELEASE_REPORT_DIR:-artifacts/release}"
report_path="$report_dir/nuget-org-post-publish-verification.md"
project_dir="$verify_root/SekibanWasmRuntime.NuGetOrgVerify"

expected_packages=(
  "Sekiban.Dcb.WasmRuntime"
  "Sekiban.Dcb.WasmRuntime.Remote"
  "Sekiban.Dcb.WasmRuntime.Wasmtime"
)

rm -rf "$verify_root"
mkdir -p "$project_dir" "$report_dir"

cat > "$project_dir/NuGet.config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

cat > "$project_dir/SekibanWasmRuntime.NuGetOrgVerify.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <NoWarn>\$(NoWarn);NU1901;NU1902;NU1903;NU1904</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Sekiban.Dcb.WasmRuntime" Version="$package_version" />
    <PackageReference Include="Sekiban.Dcb.WasmRuntime.Remote" Version="$package_version" />
    <PackageReference Include="Sekiban.Dcb.WasmRuntime.Wasmtime" Version="$package_version" />
  </ItemGroup>
</Project>
EOF

cat > "$project_dir/Program.cs" <<'EOF'
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

Type[] publicSurface =
[
    typeof(ISerializedDcbClient),
    typeof(ISerializedQueryClient),
    typeof(ISekibanWasmExecutor),
    typeof(SerializedQueryRequest),
    typeof(HttpSerializedDcbClient),
    typeof(HttpSerializedQueryClient),
    typeof(SerializedDcbClientOptions),
    typeof(WasmtimeHostOptions),
    typeof(WasmtimeRuntime)
];

Console.WriteLine(string.Join(Environment.NewLine, publicSurface.Select(t => t.FullName)));
EOF

restore_log="$verify_root/restore.log"
build_log="$verify_root/build.log"

write_report() {
  local status="$1"
  local detail="$2"
  {
    printf '# NuGet.org Post-Publish Verification\n\n'
    printf 'Package version: `%s`\n' "$package_version"
    printf 'Package source: `https://api.nuget.org/v3/index.json`\n'
    printf 'Consumer project: `%s`\n\n' "$project_dir/SekibanWasmRuntime.NuGetOrgVerify.csproj"
    printf '## Package Coverage\n\n'
    printf '| Package | Version |\n'
    printf '| --- | --- |\n'
    for package_id in "${expected_packages[@]}"; do
      printf '| `%s` | `%s` |\n' "$package_id" "$package_version"
    done
    printf '\n## Result\n\n'
    printf '%s: %s\n\n' "$status" "$detail"
    printf '## Commands\n\n'
    printf '```bash\n'
    printf 'dotnet restore %q --configfile %q --no-cache --nologo\n' "$project_dir/SekibanWasmRuntime.NuGetOrgVerify.csproj" "$project_dir/NuGet.config"
    printf 'dotnet build %q -c Release --no-restore --nologo\n' "$project_dir/SekibanWasmRuntime.NuGetOrgVerify.csproj"
    printf '```\n\n'
    if [[ -s "$restore_log" ]]; then
      printf '## Restore Output\n\n'
      printf '```text\n'
      sed -e 's/\r$//' "$restore_log"
      printf '```\n\n'
    fi
    if [[ -s "$build_log" ]]; then
      printf '## Build Output\n\n'
      printf '```text\n'
      sed -e 's/\r$//' "$build_log"
      printf '```\n\n'
    fi
    printf '## Failure Handling\n\n'
    printf -- '- If restore reports `NU1101` or `NU1102`, wait for NuGet.org indexing and retry before announcing availability.\n'
    printf -- '- If only some package IDs restore, treat the release as partially published and hold consumer-facing announcements until all three package IDs restore at the same version.\n'
    printf -- '- If restore succeeds but build fails, inspect dependency or public API errors before marking the release verified.\n'
    printf -- '- Do not publish replacement packages with the same version; NuGet package versions are immutable.\n'
  } > "$report_path"
}

if ! dotnet restore "$project_dir/SekibanWasmRuntime.NuGetOrgVerify.csproj" \
  --configfile "$project_dir/NuGet.config" \
  --no-cache \
  --nologo \
  > "$restore_log" 2>&1; then
  write_report "FAIL" "NuGet.org restore failed; packages may be missing, delayed by indexing, or unavailable at the requested version."
  cat "$report_path" >&2
  exit 1
fi

if ! dotnet build "$project_dir/SekibanWasmRuntime.NuGetOrgVerify.csproj" \
  -c Release \
  --no-restore \
  --nologo \
  > "$build_log" 2>&1; then
  write_report "FAIL" "NuGet.org restore succeeded, but the generated consumer project did not build."
  cat "$report_path" >&2
  exit 1
fi

write_report "PASS" "All three public preview packages restored from NuGet.org and built in a generated consumer project."
cat "$report_path"
