# Executed mixed-version probe

| Direction | Loaded package | Observed result |
| --- | --- | --- |
| 10.2.2-linked client fixture → 10.12.0 runtime baseline | Sekiban.Dcb.WithoutResult 10.2.2 → Sekiban.Dcb.Core 10.12.0 | build and dependency resolution PASS |
| 10.12.0-linked runtime → 10.2.2-linked client fixture | Sekiban.Dcb.Core 10.12.0 → Sekiban.Dcb.WithoutResult 10.2.2 | build and dependency resolution PASS |

Both sides were loaded by the .NET build/restore graph; no source inspection was
used as evidence. The old side is intentionally the 10.2.2 compatibility fixture.
