#!/usr/bin/env python3
"""Synchronize cargo-sekiban assets from the maintained Rust source trees.

The checked-in templates are generated assets.  The source samples remain the
source of truth; this script performs the deterministic copy, standalone path
rewrite, project-name tokenization, and dev-mode vendoring.  ``--check`` builds
an expected tree in a temporary directory and compares it byte-for-byte and
mode-for-mode without changing the working tree.
"""

from __future__ import annotations

import argparse
import filecmp
import re
import shutil
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PACKAGE_ROOT = ROOT / "src/tools/cargo-sekiban"
TEMPLATES_ROOT = PACKAGE_ROOT / "src" / "templates"
REGISTRY_SAMPLE = ROOT / "src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider"
DEV_SAMPLE = ROOT / "src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider"
RUST_SOURCE_ROOT = ROOT / "src/wasm-projectors/rust"

TOKEN_NAME = "__SEKIBAN_PROJECT_NAME__"
TOKEN_KEBAB = "__SEKIBAN_PROJECT_KEBAB__"
TOKEN_SNAKE = "__SEKIBAN_PROJECT_SNAKE__"
TOKEN_PASCAL = "__SEKIBAN_PROJECT_PASCAL__"
VENDOR_CRATES = (
    "sekiban-core",
    "sekiban-derive",
    "sekiban-wasm",
    "sekiban-mv",
    "sekiban-executor",
    "domain",
)
EXCLUDED_DIRECTORIES = {
    ".aspire",
    ".build",
    ".git",
    ".swiftpm",
    "_build",
    "artifacts",
    "bin",
    "build",
    "dist",
    "node_modules",
    "obj",
    "reports",
    "target",
}
EXCLUDED_FILES = {"Cargo.lock", ".codegen-hash", "go.work", "go.work.sum", "package-lock.json"}
ROOT_PATTERN = re.compile(
    r'ROOT="\$\(cd "\$\(dirname "\$\{BASH_SOURCE\[0\]\}"\)/\.\./\.\./\.\./\.\." && pwd\)"'
)


class SyncError(RuntimeError):
    """A source/template synchronization contract failed."""


def write_text(path: Path, text: str) -> None:
    path.write_bytes(text.encode("utf-8"))


def replacements(mode: str) -> tuple[tuple[str, str], ...]:
    common = (
        ("CratesIoRsDecider", TOKEN_PASCAL),
        ("PublicContainerRsDecider", TOKEN_PASCAL),
        ("crates_io_rs_decider", TOKEN_SNAKE),
        ("public_container_rs_decider", TOKEN_SNAKE),
        ("crates-io-rs-decider", TOKEN_KEBAB),
        ("public-container-rs-decider", TOKEN_KEBAB),
        ("CratesIo.RsDecider", TOKEN_NAME),
        ("PublicContainer.RsDecider", TOKEN_NAME),
        ("CratesIo Rust Decider", f"{TOKEN_NAME} Rust Decider"),
        ("Public Container Rust Decider", f"{TOKEN_NAME} Rust Decider"),
    )
    if mode == "dev":
        return common + (
            (
                "repository-local Rust crates under src/wasm-projectors/rust",
                "vendored Rust crates under vendor/",
            ),
            ("src/wasm-projectors/rust", "vendor"),
        )
    return common + (
        (
            "src/wasm-projectors/rust/sekiban-*",
            "repository-local Sekiban Rust path dependencies",
        ),
        ("src/wasm-projectors/rust", "repository-local Rust sources"),
    )


def tokenize(text: str, mode: str) -> str:
    for old, new in replacements(mode):
        text = text.replace(old, new)
    return text


def portabilize(text: str, sample_name: str, relative: Path) -> str:
    if relative.suffix == ".sh":
        text = ROOT_PATTERN.sub(
            'ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"', text
        )
        text = re.sub(
            r'^SAMPLE_DIR="src/samples/[^"]+"$',
            'SAMPLE_DIR="."',
            text,
            flags=re.MULTILINE,
        )
        text = text.replace(f"src/samples/{sample_name}/", "")
        if relative.name == "smoke.sh":
            cargo_marker = "command -v cargo >/dev/null 2>&1 || skip \"cargo not found.\""
            target_guard = (
                "\n"
                'target_libdir=""\n'
                'if command -v rustc >/dev/null 2>&1; then\n'
                '  target_libdir="$(rustc --target wasm32-wasip1 --print target-libdir 2>/dev/null || true)"\n'
                "fi\n"
                'if [[ -z "$target_libdir" ]] || ! compgen -G "$target_libdir/libcore-*.rlib" >/dev/null; then\n'
                '  skip "active rustc cannot use the wasm32-wasip1 target."\n'
                "fi\n"
            )
            if cargo_marker not in text:
                raise SyncError(f"smoke script shape changed; cannot add target guard to {relative}")
            text = text.replace(cargo_marker, cargo_marker + target_guard, 1)
    elif relative.name == "Program.cs":
        text = text.replace(
            'Path.Combine(builder.AppHostDirectory, "..", "..", "..", "..")',
            'Path.Combine(builder.AppHostDirectory, "..")',
        )
        text = text.replace(f"src/samples/{sample_name}/", "")
    elif relative.name == "README.md":
        text = text.replace(f"src/samples/{sample_name}/", "")
    return text


def copy_file(source: Path, destination: Path, mode: str, sample_name: str, source_root: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    data = source.read_bytes()
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        destination.write_bytes(data)
    else:
        text = portabilize(text, sample_name, source.relative_to(source_root))
        write_text(destination, tokenize(text, mode))
    shutil.copymode(source, destination)


def copy_tree(source_root: Path, destination_root: Path, mode: str, sample_name: str) -> None:
    if not source_root.is_dir():
        raise SyncError(f"source tree does not exist: {source_root}")
    for source in sorted(source_root.rglob("*")):
        relative = source.relative_to(source_root)
        if any(part in EXCLUDED_DIRECTORIES for part in relative.parts):
            continue
        if source.name in EXCLUDED_FILES or source.name.endswith(".wasm"):
            continue
        destination_relative = Path(
            *(
                part.replace("CratesIoRsDecider", TOKEN_PASCAL)
                .replace("PublicContainerRsDecider", TOKEN_PASCAL)
                for part in relative.parts
            )
        )
        destination = destination_root / storage_relative(destination_relative)
        if source.is_dir():
            destination.mkdir(parents=True, exist_ok=True)
        elif source.is_file():
            copy_file(source, destination, mode, sample_name, source_root)


def storage_relative(relative: Path) -> Path:
    """Keep nested Cargo assets publishable without changing generated output."""
    parts = list(relative.parts)
    if parts[-1] == "Cargo.toml":
        parts[-1] = "Cargo.toml.template"
    elif parts[-1] == ".gitignore":
        parts[-1] = "gitignore.template"
    return Path(*parts)


def rewrite_registry_guard(destination_root: Path) -> None:
    path = destination_root / "scripts/verify-no-local-sekiban-paths.sh"
    text = path.read_text(encoding="utf-8")
    old = re.compile(
        r'SAMPLE_DIR="\."\n\nif rg .*?\nfi\n\n(?=# The end-to-end smoke)',
        flags=re.DOTALL,
    )
    new = r'''SAMPLE_DIR="."

scan_manifests() {
  local pattern="$1"
  if command -v rg >/dev/null 2>&1; then
    rg -ni --glob 'Cargo.toml' "$pattern" "$SAMPLE_DIR"
  else
    find "$SAMPLE_DIR" -type f -name Cargo.toml -exec grep -Eni "$pattern" {} +
  fi
}

fail_on_manifest_match() {
  local pattern="$1" message="$2" matches="" status=0
  matches="$(scan_manifests "$pattern")" || status=$?
  if [[ "$status" -gt 1 ]]; then
    echo "could not scan Cargo manifests" >&2
    exit 1
  fi
  if [[ "$status" -eq 0 && -n "$matches" ]]; then
    echo "$message" >&2
    printf '%s\n' "$matches" >&2
    exit 1
  fi
}

fail_on_manifest_match \
  'sekiban-wasm-domain' \
  'forbidden sekiban-wasm-domain dependency found'

path_matches=""
path_status=0
path_matches="$(scan_manifests 'path[[:space:]]*=')" || path_status=$?
if [[ "$path_status" -gt 1 ]]; then
  echo "could not scan Cargo manifests for path dependencies" >&2
  exit 1
fi
if [[ "$path_status" -eq 0 ]] && printf '%s\n' "$path_matches" | grep -Eiq 'wasm-projectors|sekiban-(core|derive|mv|wasm|executor|domain)'; then
  echo "forbidden local Sekiban path dependency found" >&2
  printf '%s\n' "$path_matches" >&2
  exit 1
fi

contains_pattern() {
  local pattern="$1" file="$2"
  if command -v rg >/dev/null 2>&1; then
    rg -q "$pattern" "$file"
  else
    grep -Eq "$pattern" "$file"
  fi
}

'''
    if not old.search(text):
        raise SyncError(f"registry guard shape changed; cannot rewrite {path}")
    rewritten = old.sub(lambda _match: new, text, count=1)
    old_image_check = "if ! rg -q 'ghcr\\.io/j-tech-japan/sekiban-wasm-runtime-host' \"$APPHOST_PROGRAM\"; then"
    new_image_check = "if ! contains_pattern 'ghcr\\.io/j-tech-japan/sekiban-wasm-runtime-host' \"$APPHOST_PROGRAM\"; then"
    if old_image_check not in rewritten:
        raise SyncError(f"registry AppHost image check shape changed; cannot rewrite {path}")
    write_text(path, rewritten.replace(old_image_check, new_image_check, 1))


def rewrite_dev_workspace(destination_root: Path) -> None:
    root_manifest = destination_root / "Cargo.toml.template"
    text = root_manifest.read_text(encoding="utf-8")
    match = re.search(r"members\s*=\s*\[\n(?P<members>.*?)\n\]", text, flags=re.DOTALL)
    if match is None:
        raise SyncError(f"workspace members list not found in {root_manifest}")
    members = match.group("members").rstrip()
    vendor_members = "\n".join(f'    "vendor/{crate}",' for crate in VENDOR_CRATES)
    text = text[: match.start("members")] + f"{members}\n{vendor_members}" + text[match.end("members") :]

    source_workspace = (RUST_SOURCE_ROOT / "Cargo.toml").read_text(encoding="utf-8")
    workspace_package = re.search(r"\[workspace\.package\][\s\S]*?(?=\n\[|$)", source_workspace)
    if workspace_package is None:
        raise SyncError("[workspace.package] metadata is missing from the Rust source workspace")
    text = f"{text.rstrip()}\n\n{workspace_package.group(0).rstrip()}\n"
    write_text(root_manifest, text)

    for project in ("Wasm", "Client"):
        manifest = destination_root / project / "Cargo.toml.template"
        text = manifest.read_text(encoding="utf-8")
        text = re.sub(
            r'path\s*=\s*"\.\./\.\./\.\./wasm-projectors/rust/([A-Za-z0-9_-]+)"',
            r'path = "../vendor/\1"',
            text,
        )
        write_text(manifest, text)


DEV_VENDOR_GUARD = r'''#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

required=(sekiban-core sekiban-derive sekiban-wasm sekiban-mv sekiban-executor domain)
for crate in "${required[@]}"; do
  [[ -s "vendor/$crate/Cargo.toml" ]] || {
    echo "missing vendored crate: vendor/$crate" >&2
    exit 1
  }
done

manifests=(Cargo.toml Client/Cargo.toml Wasm/Cargo.toml)
for crate in "${required[@]}"; do manifests+=("vendor/$crate/Cargo.toml"); done
manifest_matches() {
  local pattern="$1"
  if command -v rg >/dev/null 2>&1; then
    rg -n "$pattern" "${manifests[@]}"
  else
    grep -En "$pattern" "${manifests[@]}"
  fi
}

matches=""
status=0
matches="$(manifest_matches 'wasm-projectors/rust|path[[:space:]]*=[[:space:]]*"/(Users|home|private)/')" || status=$?
if [[ "$status" -gt 1 ]]; then
  echo "could not scan generated dev manifests" >&2
  exit 1
fi
if [[ "$status" -eq 0 && -n "$matches" ]]; then
  echo "generated dev workspace still contains an original-checkout or absolute path" >&2
  printf '%s\n' "$matches" >&2
  exit 1
fi

cargo metadata --manifest-path Cargo.toml --format-version 1 >/dev/null
cargo check --manifest-path Cargo.toml --workspace
echo "dev vendor guard passed: the workspace is self-contained"
'''


def write_dev_guard(destination_root: Path) -> None:
    path = destination_root / "scripts/verify-vendor.sh"
    path.parent.mkdir(parents=True, exist_ok=True)
    write_text(path, DEV_VENDOR_GUARD)
    path.chmod(0o755)


def copy_mode(mode: str, source: Path, sample_name: str, destination: Path) -> None:
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True, exist_ok=True)
    copy_tree(source, destination, mode, sample_name)
    if mode == "registry":
        rewrite_registry_guard(destination)
        return
    for crate in VENDOR_CRATES:
        copy_tree(RUST_SOURCE_ROOT / crate, destination / "vendor" / crate, mode, sample_name)
    rewrite_dev_workspace(destination)
    write_dev_guard(destination)


def sync_into(destination_root: Path) -> None:
    if destination_root.exists():
        shutil.rmtree(destination_root)
    destination_root.mkdir(parents=True, exist_ok=True)
    copy_mode(
        "registry",
        REGISTRY_SAMPLE,
        "Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider",
        destination_root / "registry",
    )
    copy_mode(
        "dev",
        DEV_SAMPLE,
        "Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider",
        destination_root / "dev",
    )
    assert_no_monorepo_residue(destination_root)


def all_files(root: Path) -> list[Path]:
    return sorted(path for path in root.rglob("*") if path.is_file())


def assert_no_monorepo_residue(root: Path) -> None:
    residue = []
    for path in all_files(root):
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        if "src/samples/" in text or "src/wasm-projectors/rust/" in text or "/../../../.." in text:
            residue.append(str(path.relative_to(root)))
    if residue:
        raise SyncError("monorepo-relative path residue found in: " + ", ".join(residue))


def compare_trees(expected: Path, actual: Path) -> list[str]:
    expected_files = {path.relative_to(expected) for path in all_files(expected)}
    actual_files = {path.relative_to(actual) for path in all_files(actual)}
    failures = [f"missing asset: {path}" for path in sorted(expected_files - actual_files)]
    failures.extend(f"unexpected asset: {path}" for path in sorted(actual_files - expected_files))
    for relative in sorted(expected_files & actual_files):
        expected_file = expected / relative
        actual_file = actual / relative
        if not filecmp.cmp(expected_file, actual_file, shallow=False):
            failures.append(f"content drift: {relative}")
        elif (expected_file.stat().st_mode & 0o777) != (actual_file.stat().st_mode & 0o777):
            failures.append(f"mode drift: {relative}")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="check the checked-in asset tree")
    args = parser.parse_args()
    if args.check:
        with tempfile.TemporaryDirectory(prefix="cargo-sekiban-sync-") as temporary:
            expected = Path(temporary) / "templates"
            sync_into(expected)
            failures = compare_trees(expected, TEMPLATES_ROOT)
        if failures:
            for failure in failures:
                print(f"FAIL: {failure}", file=sys.stderr)
            print("Run the sync script without --check to refresh assets.", file=sys.stderr)
            return 1
        print("cargo-sekiban template sync check passed")
        return 0

    sync_into(TEMPLATES_ROOT)
    print(f"cargo-sekiban templates synchronized ({len(all_files(TEMPLATES_ROOT))} files)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SyncError as error:
        print(f"sync-templates: {error}", file=sys.stderr)
        raise SystemExit(1)
