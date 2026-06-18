#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

package_version="${PACKAGE_VERSION:-${1:-1.0.0-preview.1}}"
package_version="${package_version#v}"

if [[ ! "$package_version" =~ ^1\.0\.0-preview\.[0-9A-Za-z.-]+$ ]]; then
  printf 'Package version must be 1.0.0-preview.*; got %s\n' "$package_version" >&2
  exit 1
fi

output_dir="${NUGET_OUTPUT_DIR:-artifacts/nuget}"
report_dir="${RELEASE_REPORT_DIR:-artifacts/release}"
rm -rf "$output_dir"
mkdir -p "$output_dir" "$report_dir"

projects=(
  "src/lib/Sekiban.Dcb.WasmRuntime/Sekiban.Dcb.WasmRuntime.csproj"
  "src/lib/Sekiban.Dcb.WasmRuntime.Remote/Sekiban.Dcb.WasmRuntime.Remote.csproj"
  "src/lib/Sekiban.Dcb.WasmRuntime.Wasmtime/Sekiban.Dcb.WasmRuntime.Wasmtime.csproj"
)

for project in "${projects[@]}"; do
  dotnet pack "$project" \
    -c Release \
    -o "$output_dir" \
    /p:PackageVersion="$package_version" \
    --nologo
done

python3 - "$output_dir" "$report_dir/package-inspection.md" "$package_version" <<'PY'
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

output_dir = Path(sys.argv[1])
report_path = Path(sys.argv[2])
expected_version = sys.argv[3]
expected_ids = {
    "Sekiban.Dcb.WasmRuntime",
    "Sekiban.Dcb.WasmRuntime.Remote",
    "Sekiban.Dcb.WasmRuntime.Wasmtime",
}

packages = sorted(output_dir.glob("*.nupkg"))
errors = []
rows = []
seen_ids = set()

if len(packages) != len(expected_ids):
    errors.append(f"expected {len(expected_ids)} packages, found {len(packages)}")

for package in packages:
    with zipfile.ZipFile(package) as zf:
        names = set(zf.namelist())
        nuspec_names = [name for name in names if name.endswith(".nuspec")]
        if len(nuspec_names) != 1:
            errors.append(f"{package.name}: expected one nuspec, found {len(nuspec_names)}")
            continue

        root = ET.fromstring(zf.read(nuspec_names[0]))
        ns = {"n": root.tag.split("}")[0].strip("{")} if root.tag.startswith("{") else {}
        metadata = root.find("n:metadata", ns) if ns else root.find("metadata")

        def text(name: str) -> str:
            if metadata is None:
                return ""
            element = metadata.find(f"n:{name}", ns) if ns else metadata.find(name)
            return (element.text or "").strip() if element is not None else ""

        package_id = text("id")
        version = text("version")
        readme = text("readme")
        license_text = text("license")
        repository = metadata.find("n:repository", ns) if ns and metadata is not None else (
            metadata.find("repository") if metadata is not None else None
        )
        repository_url = repository.attrib.get("url", "") if repository is not None else ""

        seen_ids.add(package_id)
        has_readme_file = "README.md" in names
        has_license_file = "LICENSE" in names
        has_lib = any(name.startswith("lib/net10.0/") and name.endswith(".dll") for name in names)

        if package_id not in expected_ids:
            errors.append(f"{package.name}: unexpected package id {package_id}")
        if version != expected_version:
            errors.append(f"{package.name}: expected version {expected_version}, got {version}")
        if readme != "README.md" or not has_readme_file:
            errors.append(f"{package.name}: package README metadata/file is missing")
        if license_text != "LICENSE" or not has_license_file:
            errors.append(f"{package.name}: package LICENSE metadata/file is missing")
        if repository_url != "https://github.com/J-Tech-Japan/SekibanWasmRuntime":
            errors.append(f"{package.name}: repository URL is missing or incorrect")
        if not has_lib:
            errors.append(f"{package.name}: no net10.0 library asset found")

        rows.append((package.name, package_id, version, str(package.stat().st_size), readme, license_text, repository_url))

missing_ids = expected_ids - seen_ids
if missing_ids:
    errors.append(f"missing package ids: {', '.join(sorted(missing_ids))}")

report_lines = [
    "# NuGet Package Inspection",
    "",
    f"Package version: `{expected_version}`",
    f"Package directory: `{output_dir}`",
    "",
    "| Package | Id | Version | Bytes | Readme | License | Repository |",
    "| --- | --- | --- | ---: | --- | --- | --- |",
]

for row in rows:
    report_lines.append("| " + " | ".join(f"`{cell}`" for cell in row) + " |")

report_lines.extend(["", "## Result", ""])
if errors:
    report_lines.append("Failed:")
    report_lines.extend(f"- {error}" for error in errors)
else:
    report_lines.append("Passed.")

report_path.write_text("\n".join(report_lines) + "\n", encoding="utf-8")

if errors:
    print(report_path.read_text(encoding="utf-8"), file=sys.stderr)
    sys.exit(1)

print(report_path.read_text(encoding="utf-8"))
PY
