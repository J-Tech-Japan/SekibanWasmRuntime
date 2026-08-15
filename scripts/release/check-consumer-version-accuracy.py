#!/usr/bin/env python3
"""Check current consumer-version assertions and packaged README metadata.

The release lane supplies the version it is about to publish.  The DCB version
is read from the produced nupkg dependency metadata, not from another source
file, and marked README assertions are checked both before and after packing.
Historical release evidence is deliberately ignored; see
docs/release/consumer-version-policy.md.
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path


MARKER_RE = re.compile(r"<!--\s*release-lane:\s*(current-[a-z-]+)\s*-->")
PREVIEW_VERSION_RE = re.compile(
    r"(?<![A-Za-z0-9.-])1\.0\.0-preview\.[0-9A-Za-z][0-9A-Za-z.-]*(?![A-Za-z0-9.-])"
)
DCB_VERSION_RE = re.compile(r"(?<![A-Za-z0-9.-])10\.[0-9]+\.[0-9]+(?![A-Za-z0-9.-])")
MARKERS = {
    "current-package-version": PREVIEW_VERSION_RE,
    "current-dcb-version": DCB_VERSION_RE,
    "current-runtime-image-version": PREVIEW_VERSION_RE,
}


def repository_root() -> Path:
    return Path(__file__).resolve().parents[2]


def tracked_markdown(root: Path) -> list[Path]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z", "--", "*.md", "README.md"],
        check=True,
        capture_output=True,
    )
    return [root / name for name in result.stdout.decode().split("\0") if name]


def display_path(path: Path, root: Path) -> str:
    try:
        return str(path.relative_to(root))
    except ValueError:
        return str(path)


def marked_assertions(path: Path, text: str, expected: dict[str, str]) -> tuple[list[str], int]:
    errors: list[str] = []
    count = 0
    shown_path = str(path)
    for line_number, line in enumerate(text.splitlines(), start=1):
        for marker in MARKER_RE.findall(line):
            count += 1
            pattern = MARKERS.get(marker)
            if pattern is None:
                errors.append(f"{shown_path}:{line_number}: unknown marker {marker!r}")
                continue
            versions = pattern.findall(line)
            expected_version = expected.get(marker)
            if not versions:
                errors.append(
                    f"{shown_path}:{line_number}: {marker} must be on the same line as its version"
                )
            elif expected_version is None:
                errors.append(f"{shown_path}:{line_number}: no expected value supplied for {marker}")
            elif any(version != expected_version for version in versions):
                actual = ", ".join(sorted(set(versions)))
                errors.append(
                    f"{shown_path}:{line_number}: {marker} has {actual!r}; expected {expected_version!r}"
                )
    return errors, count


def nuspec_metadata(zf: zipfile.ZipFile) -> tuple[str, ET.Element, dict[str, str]]:
    nuspec_names = [name for name in zf.namelist() if name.endswith(".nuspec")]
    if len(nuspec_names) != 1:
        raise ValueError(f"expected one .nuspec, found {len(nuspec_names)}")
    root = ET.fromstring(zf.read(nuspec_names[0]))
    namespace = {"n": root.tag.split("}")[0].strip("{")} if root.tag.startswith("{") else {}
    metadata = root.find("n:metadata", namespace) if namespace else root.find("metadata")
    if metadata is None:
        raise ValueError("nuspec has no metadata")

    def text(name: str) -> str:
        element = metadata.find(f"n:{name}", namespace) if namespace else metadata.find(name)
        return (element.text or "").strip() if element is not None else ""

    return text("id"), metadata, namespace


def dependency_values(metadata: ET.Element, namespace: dict[str, str]) -> list[tuple[str, str]]:
    groups = metadata.findall("n:dependencies/n:group", namespace) if namespace else metadata.findall("dependencies/group")
    values: list[tuple[str, str]] = []
    for group in groups:
        dependencies = group.findall("n:dependency", namespace) if namespace else group.findall("dependency")
        for dependency in dependencies:
            dependency_id = dependency.attrib.get("id", "")
            version = dependency.attrib.get("version", "")
            if dependency_id.startswith("Sekiban.Dcb.") and not dependency_id.startswith(
                "Sekiban.Dcb.WasmRuntime"
            ):
                values.append((dependency_id, version))
    return values


def inspect_packages(
    package_dir: Path,
    expected_package_version: str,
    expected_dcb_version: str | None,
    expected_markers: dict[str, str],
    root: Path,
) -> tuple[list[str], set[str], list[tuple[str, str]]]:
    errors: list[str] = []
    observed_dcb_versions: set[str] = set()
    package_readmes: list[tuple[str, str]] = []
    packages = sorted(package_dir.glob("*.nupkg"))
    if not packages:
        return [f"{package_dir}: no .nupkg files found"], observed_dcb_versions, package_readmes

    for package in packages:
        try:
            with zipfile.ZipFile(package) as zf:
                package_id, metadata, namespace = nuspec_metadata(zf)
                names = set(zf.namelist())

                version_element = metadata.find("n:version", namespace) if namespace else metadata.find("version")
                package_version = (version_element.text or "").strip() if version_element is not None else ""
                if package_version != expected_package_version:
                    errors.append(
                        f"{package.name}: package metadata version {package_version!r}; "
                        f"expected {expected_package_version!r}"
                    )

                if "README.md" not in names:
                    errors.append(f"{package.name}: README.md is missing from the nupkg")
                else:
                    package_readmes.append((f"{package.name}!README.md", zf.read("README.md").decode("utf-8")))

                for dependency_id, raw_version in dependency_values(metadata, namespace):
                    versions = DCB_VERSION_RE.findall(raw_version)
                    if not versions:
                        errors.append(
                            f"{package.name}: dependency {dependency_id} has no concrete DCB version in {raw_version!r}"
                        )
                    else:
                        observed_dcb_versions.update(versions)
        except (OSError, ValueError, ET.ParseError, UnicodeDecodeError, zipfile.BadZipFile) as error:
            errors.append(f"{package.name}: cannot inspect package: {error}")

    if expected_dcb_version is not None and observed_dcb_versions != {expected_dcb_version}:
        errors.append(
            f"{package_dir}: produced nupkg DCB versions are {sorted(observed_dcb_versions)!r}; "
            f"expected {[expected_dcb_version]!r}"
        )
    elif expected_dcb_version is None:
        if len(observed_dcb_versions) != 1:
            errors.append(
                f"{package_dir}: could not derive one DCB version from produced nupkgs: "
                f"{sorted(observed_dcb_versions)!r}"
            )
        else:
            expected_markers["current-dcb-version"] = next(iter(observed_dcb_versions))

    return errors, observed_dcb_versions, package_readmes


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package-version", required=True)
    parser.add_argument("--package-dir", type=Path)
    parser.add_argument("--dcb-version")
    parser.add_argument("--runtime-image-version", required=True)
    parser.add_argument("--document", action="append", type=Path, dest="documents")
    parser.add_argument("--skip-package-artifact", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = repository_root()
    expected = {
        "current-package-version": args.package_version,
        "current-dcb-version": args.dcb_version or "",
        "current-runtime-image-version": args.runtime_image_version,
    }
    errors: list[str] = []
    package_readmes: list[tuple[str, str]] = []
    observed_dcb_versions: set[str] = set()

    if not args.skip_package_artifact:
        if args.package_dir is None:
            errors.append("--package-dir is required unless --skip-package-artifact is used")
        else:
            package_errors, observed_dcb_versions, package_readmes = inspect_packages(
                args.package_dir,
                args.package_version,
                args.dcb_version,
                expected,
                root,
            )
            errors.extend(package_errors)

    if not expected["current-dcb-version"] and len(observed_dcb_versions) == 1:
        expected["current-dcb-version"] = next(iter(observed_dcb_versions))
    if not expected["current-dcb-version"]:
        errors.append("no DCB version is available; provide --dcb-version or a produced nupkg")

    documents = args.documents or tracked_markdown(root)
    marker_counts = {marker: 0 for marker in MARKERS}
    for document in documents:
        if not document.is_file():
            errors.append(f"{display_path(document, root)}: document does not exist")
            continue
        try:
            text = document.read_text(encoding="utf-8")
        except UnicodeDecodeError as error:
            errors.append(f"{display_path(document, root)}: cannot read UTF-8: {error}")
            continue
        document_errors, count = marked_assertions(document, text, expected)
        errors.extend(document_errors)
        for marker in MARKER_RE.findall(text):
            if marker in marker_counts:
                marker_counts[marker] += 1

    for marker, count in marker_counts.items():
        if count == 0 and not args.documents:
            errors.append(f"tracked documents contain no {marker} assertion")

    for logical_path, text in package_readmes:
        package_errors, _ = marked_assertions(Path(logical_path), text, expected)
        errors.extend(package_errors)

    print(f"Package version: {args.package_version}")
    print(f"DCB version from produced package metadata: {expected['current-dcb-version'] or '(none)'}")
    print(f"Runtime image version: {args.runtime_image_version}")
    print(f"Checked marked documents: {len(documents)}")
    print(f"Checked packaged READMEs: {len(package_readmes)}")
    if errors:
        print("FAIL: consumer version accuracy assertions failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("PASS: current consumer documents and packaged README assertions match the release inputs.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
