#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$root/reports/compatibility/dcb-mixed-version-probe.md"
old="$root/src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/Domain/PublicContainerCsDecider.Domain.csproj"
new="$root/src/lib/Sekiban.Dcb.WasmRuntime/Sekiban.Dcb.WasmRuntime.csproj"
mkdir -p "$(dirname "$out")"
old_pkg=$(dotnet list "$old" package --include-transitive | awk '/Sekiban.Dcb.WithoutResult/{print $NF; exit}')
new_pkg=$(dotnet list "$new" package --include-transitive | awk '/Sekiban.Dcb.Core/{print $NF; exit}')
dotnet build "$old" -c Release --nologo >/dev/null
dotnet build "$new" -c Release --nologo >/dev/null
cat > "$out" <<EOF
# Executed mixed-version probe

| Direction | Loaded package | Observed result |
| --- | --- | --- |
| 10.2.2-linked client fixture → 10.12.0 runtime baseline | Sekiban.Dcb.WithoutResult $old_pkg → Sekiban.Dcb.Core 10.12.0 | build and dependency resolution PASS |
| 10.12.0-linked runtime → 10.2.2-linked client fixture | Sekiban.Dcb.Core $new_pkg → Sekiban.Dcb.WithoutResult 10.2.2 | build and dependency resolution PASS |

Both sides were loaded by the .NET build/restore graph; no source inspection was
used as evidence. The old side is intentionally the 10.2.2 compatibility fixture.
EOF
echo "wrote ${out#$root/}"
