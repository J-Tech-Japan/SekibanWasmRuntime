#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

patterns=(
  '-----BEGIN (RSA |DSA |EC |OPENSSH )?PRIVATE KEY-----'
  'github_pat_[A-Za-z0-9_]{20,}'
  'gh[pousr]_[A-Za-z0-9_]{20,}'
  'AKIA[0-9A-Z]{16}'
  'xox[baprs]-[A-Za-z0-9-]{20,}'
)

matches=""
for pattern in "${patterns[@]}"; do
  found="$(git grep -nIE -e "$pattern" -- . ':(exclude)submodules/**' ':(exclude)external/**' || true)"
  if [[ -n "$found" ]]; then
    matches+="$found"$'\n'
  fi
done

if [[ -n "$matches" ]]; then
  printf 'High-confidence secret patterns were found:\n%s\n' "$matches" >&2
  exit 1
fi

printf 'secret scan passed\n'
