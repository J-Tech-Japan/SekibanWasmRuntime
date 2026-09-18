#!/usr/bin/env python3
"""Check the C# template pins against the current release-lane contract.

The generated consumer projects intentionally opt out of this repository's
central package management. Keep their explicit pins, the Templates workflow
inputs/fallbacks, and the current consumer-document markers synchronized so a
baseline refresh cannot leave a template that restores a different dependency
line than the release lane advertises.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


EXPECTED_DCB_VERSION = "10.22.0"
EXPECTED_RUNTIME_PACKAGE_VERSION = "1.0.0-preview.7"

DOMAIN_PROJECT = Path(
    "templates/Sekiban.Dcb.WasmRuntime.Templates/content/"
    "Sekiban.Dcb.WasmRuntime.Aspire.Decider/SekibanDcbDecider.Domain/"
    "SekibanDcbDecider.Domain.csproj"
)
APPHOST_PROJECT = Path(
    "templates/Sekiban.Dcb.WasmRuntime.Templates/content/"
    "Sekiban.Dcb.WasmRuntime.Aspire.Decider/SekibanDcbDecider.AppHost/"
    "SekibanDcbDecider.AppHost.csproj"
)
TEMPLATE_CONFIG = Path(
    "templates/Sekiban.Dcb.WasmRuntime.Templates/content/"
    "Sekiban.Dcb.WasmRuntime.Aspire.Decider/.template.config/template.json"
)
TEMPLATES_WORKFLOW = Path(".github/workflows/release-templates-preview.yml")
CURRENT_DOCUMENTS = (
    Path("README.md"),
    Path("docs/public-packages.md"),
    Path("docs/quickstart.md"),
    Path("docs/nuget/package-readme.md"),
    Path("docs/nuget/aspire-package-readme.md"),
    Path("docs/nuget/templates-package-readme.md"),
)

PREVIEW_VERSION_RE = re.compile(
    r"(?<![A-Za-z0-9.-])1\.0\.0-preview\.[0-9A-Za-z][0-9A-Za-z.-]*(?![A-Za-z0-9.-])"
)
DCB_VERSION_RE = re.compile(r"(?<![A-Za-z0-9.-])10\.[0-9]+\.[0-9]+(?![A-Za-z0-9.-])")


class DriftError(Exception):
    """A contract assertion failed."""


def read(root: Path, relative: Path) -> str:
    path = root / relative
    try:
        return path.read_text(encoding="utf-8")
    except OSError as error:
        raise DriftError(f"{relative}: cannot read: {error}") from error


def one_match(text: str, pattern: str, label: str) -> str:
    matches = re.findall(pattern, text, flags=re.MULTILINE)
    if len(matches) != 1:
        raise DriftError(f"{label}: expected one match, found {len(matches)}")
    return matches[0]


def workflow_input_default(text: str, input_name: str) -> str:
    lines = text.splitlines()
    input_line = re.compile(rf"^\s{{6}}{re.escape(input_name)}:\s*$")
    default_line = re.compile(r"^\s{8}default:\s*'([^']+)'\s*$")
    for index, line in enumerate(lines):
        if not input_line.match(line):
            continue
        defaults: list[str] = []
        for child in lines[index + 1 :]:
            if child.strip() and len(child) - len(child.lstrip()) < 8:
                break
            match = default_line.match(child)
            if match:
                defaults.append(match.group(1))
        if len(defaults) != 1:
            raise DriftError(
                f"workflow input {input_name} default: expected one match, found {len(defaults)}"
            )
        return defaults[0]
    raise DriftError(f"workflow input {input_name}: block not found")


def workflow_env_fallback(text: str, variable: str) -> str:
    return one_match(
        text,
        rf"^\s*{re.escape(variable)}:\s*\$\{{\{{.*\|\|\s*'([^']+)'\s*\}}\}}\s*$",
        f"workflow {variable} fallback",
    )


def check_equal(label: str, actual: str, expected: str, failures: list[str]) -> None:
    if actual == expected:
        print(f"PASS {label}: {actual}")
    else:
        print(f"FAIL {label}: {actual!r}; expected {expected!r}")
        failures.append(label)


def check_markers(root: Path, failures: list[str]) -> None:
    marker_specs = (
        ("current-package-version", PREVIEW_VERSION_RE, EXPECTED_RUNTIME_PACKAGE_VERSION),
        ("current-dcb-version", DCB_VERSION_RE, EXPECTED_DCB_VERSION),
    )
    for relative in CURRENT_DOCUMENTS:
        text = read(root, relative)
        for marker, pattern, expected in marker_specs:
            lines = [line for line in text.splitlines() if f"release-lane: {marker}" in line]
            label = f"{relative} {marker}"
            if len(lines) != 1:
                print(f"FAIL {label}: expected one marked line, found {len(lines)}")
                failures.append(label)
                continue
            versions = pattern.findall(lines[0])
            if len(versions) != 1:
                print(f"FAIL {label}: expected one concrete version, found {versions!r}")
                failures.append(label)
                continue
            check_equal(label, versions[0], expected, failures)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[2],
        help="repository root (used by the fixture regression test)",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = args.root.resolve()
    failures: list[str] = []

    domain = read(root, DOMAIN_PROJECT)
    apphost = read(root, APPHOST_PROJECT)
    config = read(root, TEMPLATE_CONFIG)
    workflow = read(root, TEMPLATES_WORKFLOW)

    check_equal(
        "generated Domain Sekiban.Dcb.WithoutResult",
        one_match(
            domain,
            r'<PackageReference\s+Include="Sekiban\.Dcb\.WithoutResult"\s+Version="([^"]+)"\s*/>',
            str(DOMAIN_PROJECT),
        ),
        EXPECTED_DCB_VERSION,
        failures,
    )
    check_equal(
        "generated AppHost Sekiban.Dcb.WasmRuntime.Aspire",
        one_match(
            apphost,
            r'<PackageReference\s+Include="Sekiban\.Dcb\.WasmRuntime\.Aspire"\s+Version="([^"]+)"\s*/>',
            str(APPHOST_PROJECT),
        ),
        EXPECTED_RUNTIME_PACKAGE_VERSION,
        failures,
    )

    check_equal(
        "template shortName",
        one_match(config, r'"shortName"\s*:\s*"([^"]+)"', str(TEMPLATE_CONFIG)),
        "sekiban-wasm-decider",
        failures,
    )
    check_equal(
        "template sourceName",
        one_match(config, r'"sourceName"\s*:\s*"([^"]+)"', str(TEMPLATE_CONFIG)),
        "SekibanDcbDecider",
        failures,
    )
    check_equal(
        "IncludeTests default",
        one_match(
            config,
            r'(?s)"IncludeTests"\s*:\s*\{.*?"defaultValue"\s*:\s*"([^"]+)"',
            str(TEMPLATE_CONFIG),
        ),
        "true",
        failures,
    )

    check_equal(
        "workflow runtime_package_version input default",
        workflow_input_default(workflow, "runtime_package_version"),
        EXPECTED_RUNTIME_PACKAGE_VERSION,
        failures,
    )
    check_equal(
        "workflow RUNTIME_PACKAGE_VERSION fallback",
        workflow_env_fallback(workflow, "RUNTIME_PACKAGE_VERSION"),
        EXPECTED_RUNTIME_PACKAGE_VERSION,
        failures,
    )
    check_equal(
        "workflow dcb_version input default",
        workflow_input_default(workflow, "dcb_version"),
        EXPECTED_DCB_VERSION,
        failures,
    )
    check_equal(
        "workflow SEKIBAN_DCB_VERSION fallback",
        workflow_env_fallback(workflow, "SEKIBAN_DCB_VERSION"),
        EXPECTED_DCB_VERSION,
        failures,
    )

    check_markers(root, failures)

    if failures:
        print(f"FAIL: {len(failures)} template version/UX drift assertion(s) failed.", file=sys.stderr)
        return 1
    print("PASS: template pins, UX contract, release-lane defaults, and current docs agree.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
