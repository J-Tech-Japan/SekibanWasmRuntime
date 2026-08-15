#!/usr/bin/env bash

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
suite_root="$repo_root/conformance/serialized-dcb-v1"
[[ -f "$suite_root/suite.py" ]] || {
  printf 'suite not found: %s\n' "$suite_root" >&2
  exit 1
}

forbidden='pythonnet|import[[:space:]]+clr|\.dll([[:space:]]|$)|\.csproj([[:space:]]|$)|dotnet|Sekiban\.Dcb|CoreGeneral|submodules/Sekiban|ProjectReference|PackageReference'
matches="$(rg -n -i "$forbidden" "$suite_root" -g '*.py' -g '*.sh' -g '*.json' || true)"
[[ -z "$matches" ]] || {
  printf 'forbidden implementation dependency found:\n%s\n' "$matches" >&2
  exit 1
}

artifact_paths="$(rg --files "$suite_root" | rg '\.(dll|csproj|sln|slnx)$' || true)"
[[ -z "$artifact_paths" ]] || {
  printf 'compiled/project artifact found in portable suite:\n%s\n' "$artifact_paths" >&2
  exit 1
}

python3 -c '
import ast
import pathlib
import sys

allowed = {
    "argparse", "base64", "binascii", "concurrent", "datetime", "http", "json", "os", "pathlib",
    "sys", "threading", "typing", "urllib", "uuid", "__future__",
}
path = pathlib.Path(sys.argv[1])
tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
imports = []
for node in ast.walk(tree):
    if isinstance(node, ast.Import):
        imports.extend(alias.name.split(".")[0] for alias in node.names)
    elif isinstance(node, ast.ImportFrom) and node.module:
        imports.append(node.module.split(".")[0])
unknown = sorted(set(imports) - allowed)
if unknown:
    raise SystemExit("non-standard or undeclared imports: " + ", ".join(unknown))
' "$suite_root/suite.py"

printf 'STATIC_ISOLATION=PASS suite=%s imports=standard-library-only artifacts=none\n' "$suite_root"
