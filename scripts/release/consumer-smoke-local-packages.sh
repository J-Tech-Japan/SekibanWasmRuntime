#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

repo_root="$(pwd)"
package_version="${PACKAGE_VERSION:-${1:-1.0.0-preview.1}}"
package_version="${package_version#v}"

if [[ ! "$package_version" =~ ^1\.0\.0-preview\.[0-9A-Za-z.-]+$ ]]; then
  printf 'Package version must be 1.0.0-preview.*; got %s\n' "$package_version" >&2
  exit 1
fi

nuget_dir="${NUGET_OUTPUT_DIR:-artifacts/nuget}"
smoke_root="${CONSUMER_SMOKE_DIR:-artifacts/consumer-smoke}"
report_dir="${RELEASE_REPORT_DIR:-artifacts/release}"
report_path="$report_dir/consumer-smoke-local-packages.md"
project_dir="$smoke_root/SekibanWasmRuntime.ConsumerSmoke"

expected_packages=(
  "Sekiban.Dcb.WasmRuntime"
  "Sekiban.Dcb.WasmRuntime.Remote"
  "Sekiban.Dcb.WasmRuntime.Wasmtime"
  "Sekiban.Dcb.WasmRuntime.Aspire"
)

rm -rf "$smoke_root"
mkdir -p "$project_dir" "$report_dir"

errors=()
for package_id in "${expected_packages[@]}"; do
  package_path="$nuget_dir/$package_id.$package_version.nupkg"
  if [[ ! -f "$package_path" ]]; then
    errors+=("missing package: $package_path")
  fi
done

if (( ${#errors[@]} > 0 )); then
  {
    printf '# NuGet Consumer Smoke\n\n'
    printf 'Package version: `%s`\n\n' "$package_version"
    printf '## Result\n\n'
    printf 'Failed before restore:\n'
    for error in "${errors[@]}"; do
      printf -- '- %s\n' "$error"
    done
  } > "$report_path"
  cat "$report_path" >&2
  exit 1
fi

cat > "$project_dir/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-preview" value="$(cd "$nuget_dir" && pwd)" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

cat > "$project_dir/SekibanWasmRuntime.ConsumerSmoke.csproj" <<EOF
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
    <PackageReference Include="Sekiban.Dcb.WasmRuntime.Aspire" Version="$package_version" />
  </ItemGroup>
</Project>
EOF

cat > "$project_dir/Program.cs" <<'EOF'
using Aspire.Hosting;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

var query = new SerializedQueryRequest("ConsumerSmokeQuery", "{}");
var commandOptions = new SerializedCommandOptions(DryRun: true, WaitForSortableUniqueId: null);
var module = new WasmModuleRef(
    ProjectorName: "ConsumerSmokeProjector",
    ModulePath: "consumer-smoke.wasm",
    AbiKind: "wasi-preview2",
    ModuleVersion: "1.0.0-preview",
    ProjectorVersion: "1.0.0-preview");
var remoteOptions = new SerializedDcbClientOptions { BaseUrl = "https://localhost:5001" };
var runnerOptions = new RemoteRunnerOptions { Endpoint = "/api/sekiban/serialized/command" };
var wasmtimeOptions = new WasmtimeHostOptions
{
    DefaultModulePath = module.ModulePath,
    EnableWarmup = false
};
var aspireOptions = new SekibanWasmRuntimeOptions
{
    WasmModulePath = "/app/modules/consumer-smoke.wasm",
    HostPort = 58080,
};

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
    typeof(WasmtimeRuntime),
    typeof(SekibanWasmRuntimeOptions),
    typeof(SekibanWasmRuntimeBuilderExtensions)
];

Console.WriteLine(string.Join(
    Environment.NewLine,
    publicSurface.Select(type => type.FullName)));
Console.WriteLine(query.QueryType);
Console.WriteLine(commandOptions.DryRun);
Console.WriteLine(remoteOptions.BaseUrl);
Console.WriteLine(runnerOptions.Endpoint);
Console.WriteLine(wasmtimeOptions.DefaultModulePath);
Console.WriteLine($"{aspireOptions.Image}:{aspireOptions.Tag} {aspireOptions.WasmModulePath} {aspireOptions.HostPort}");
EOF

restore_log="$smoke_root/restore.log"
build_log="$smoke_root/build.log"

sanitize_output() {
  sed -e 's/\r$//' -e "s#${repo_root}#<repo>#g"
}

write_failure_report() {
  local failed_step="$1"
  {
    printf '# NuGet Consumer Smoke\n\n'
    printf 'Package version: `%s`\n' "$package_version"
    printf 'Package directory: `%s`\n' "$nuget_dir"
    printf 'Consumer project: `%s`\n\n' "$project_dir/SekibanWasmRuntime.ConsumerSmoke.csproj"
    printf '## Result\n\n'
    printf 'Failed during `%s`. Broken package IDs, missing package files, invalid dependency references, or public API regressions can fail this smoke.\n\n' "$failed_step"
    if [[ -s "$restore_log" ]]; then
      printf '## Restore Output\n\n'
      printf '```text\n'
      sanitize_output < "$restore_log"
      printf '```\n\n'
    fi
    if [[ -s "$build_log" ]]; then
      printf '## Build Output\n\n'
      printf '```text\n'
      sanitize_output < "$build_log"
      printf '```\n'
    fi
  } > "$report_path"
  cat "$report_path" >&2
}

if ! dotnet restore "$project_dir/SekibanWasmRuntime.ConsumerSmoke.csproj" \
  --configfile "$project_dir/NuGet.config" \
  --nologo \
  > "$restore_log" 2>&1; then
  write_failure_report "dotnet restore"
  exit 1
fi

if ! dotnet build "$project_dir/SekibanWasmRuntime.ConsumerSmoke.csproj" \
  -c Release \
  --no-restore \
  --nologo \
  > "$build_log" 2>&1; then
  write_failure_report "dotnet build"
  exit 1
fi

{
  printf '# NuGet Consumer Smoke\n\n'
  printf 'Package version: `%s`\n' "$package_version"
  printf 'Package directory: `%s`\n' "$nuget_dir"
  printf 'Consumer project: `%s`\n\n' "$project_dir/SekibanWasmRuntime.ConsumerSmoke.csproj"
  printf '## Referenced Packages\n\n'
  printf '| Package | Version | Source |\n'
  printf '| --- | --- | --- |\n'
  for package_id in "${expected_packages[@]}"; do
    printf '| `%s` | `%s` | `%s` |\n' "$package_id" "$package_version" "$nuget_dir/$package_id.$package_version.nupkg"
  done
  printf '\n## Commands\n\n'
  printf '```bash\n'
  printf 'dotnet restore %q --configfile %q --nologo\n' "$project_dir/SekibanWasmRuntime.ConsumerSmoke.csproj" "$project_dir/NuGet.config"
  printf 'dotnet build %q -c Release --no-restore --nologo\n' "$project_dir/SekibanWasmRuntime.ConsumerSmoke.csproj"
  printf '```\n\n'
  printf '## Result\n\n'
  printf 'Passed. The generated consumer project restored and built against the locally packed preview packages.\n\n'
  printf '## Package Selection Guidance\n\n'
  printf 'See `docs/quickstart.md` and `docs/nuget/package-readme.md` for package selection guidance.\n\n'
  printf '## Restore Output\n\n'
  printf '```text\n'
  sanitize_output < "$restore_log"
  printf '```\n\n'
  printf '## Build Output\n\n'
  printf '```text\n'
  sanitize_output < "$build_log"
  printf '```\n'
} > "$report_path"

cat "$report_path"
