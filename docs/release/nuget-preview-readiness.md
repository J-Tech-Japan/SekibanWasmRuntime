# NuGet Preview 1.0.0-preview.3 Readiness

This is the release-readiness record for the Sekiban.Dcb 10.8.0 dependency
refresh tracked by [#253]. It prepares the three SekibanWasmRuntime NuGet
packages for `1.0.0-preview.3`; it does not create a GitHub Release or publish
packages.

## Dependency Baseline

- Every centrally managed `Sekiban.Dcb.*` package in
  `Directory.Packages.props` is pinned to `10.8.0`.
- `submodules/Sekiban` points to tag `dcb-v10.8.0`, commit
  `e264a2ed1eee7c6dc29558742a0963fdb9b3cc9c`.
- The published `Sekiban.Dcb.Core` 10.8.0 package identifies the same repository
  commit in both its NuGet metadata and assembly informational version. This
  keeps projects that consume NuGet packages binary-aligned with projects that
  reference the submodule source.

`Sekiban.Dcb.WasmRuntime`, `Sekiban.Dcb.WasmRuntime.Remote`, and
`Sekiban.Dcb.WasmRuntime.Wasmtime` version `1.0.0-preview.3` must therefore carry
the 10.8.0 dependency baseline.

## Serialized Commit Ground Truth

The contract was measured before changing any emit or accept path. The evidence
came from the actual `Sekiban.Dcb.Core` 10.8.0 package restored from nuget.org,
not from issue wording or the checked-out submodule:

```bash
dnx dotnet-inspect -y -- diff \
  --package Sekiban.Dcb.Core@10.7.0..10.8.0 --oneline
dnx dotnet-inspect -y -- find '*SerializedCommit*' \
  --package Sekiban.Dcb.Core@10.8.0 --oneline
```

The API diff reported **17 additive changes and no breaking changes**. The
serialized-commit types remain present. Reflection over the real 10.8.0
assembly found:

- `VersionedSerializedCommitRequest`: `Version`, `EventCandidates`,
  `ConsistencyTags`.
- `SerializedCommitRequest`: `EventCandidates`, `ConsistencyTags`.
- `SerializableEventCandidate`: `Payload` (`byte[]`), `EventPayloadName`,
  per-candidate `Tags`.
- `ConsistencyTagEntry`: `Tag`, `LastSortableUniqueId`.

Serializing a real `VersionedSerializedCommitRequest` with the package's own
`SerializedCommitWireContract.SerializeToUtf8Bytes` produced:

```json
{
  "version": 1,
  "eventCandidates": [
    {
      "payload": "eyJmb3JlY2FzdElkIjoiZi0xIn0=",
      "eventPayloadName": "WeatherForecastCreated",
      "tags": ["weather:f-1"]
    }
  ],
  "consistencyTags": [
    { "tag": "weather:f-1", "lastSortableUniqueId": "0639" }
  ]
}
```

Deserializing those same bytes with
`SerializedCommitWireContract.Options` restored version `1`, the event payload
name, the per-candidate tag, and the consistency tag. The finding is therefore
**unchanged from 10.7.0**: 10.8.0 still uses the V1 envelope, base64 `payload`,
`eventPayloadName`, per-candidate `tags`, and `consistencyTags`. There is no
`events` / `payloadJson` / `eventType` rename and no root-level commit tag list.

Because the real contract did not change, this upgrade deliberately makes no
emit/accept shape or typed-error behavior change. The existing black-box tests
remain the executable guard against drift.

## Mixed-Version Behavior

The 10.8.0 refresh does not introduce a new envelope version:

| Client → server | Behavior |
| --- | --- |
| 10.8.0-line client (V1) → 10.8.0-line server | Accepted; this is the primary path for `1.0.0-preview.3`. |
| 10.7.0-line client (V1) → 10.8.0-line server | Accepted; the wire contract is unchanged. |
| Pre-10.7 client (legacy envelope without `version`) → 10.8.0-line server | Accepted and lifted losslessly to V1. |
| 10.8.0-line client (V1) → pre-10.7 server | Accepted by servers that ignore the unknown `version` property and bind the unchanged candidate fields. |
| Unsupported, duplicated, case-variant, or non-integer version | Rejected before write with the existing typed HTTP 400 error. |

Package/source mixing inside one process is not supported: all
`Sekiban.Dcb.*` binaries must resolve to the same 10.8.0 baseline. The
source-compatible API additions between releases do not make a 10.7.0 assembly
binary-equivalent to 10.8.0.

## Verification and Release Boundary

The required verification for this change is:

```bash
dotnet build src/SekibanWasmRuntime.slnx -c Release
dotnet test src/SekibanWasmRuntime.slnx -c Release
bash scripts/smoke-runtime-compose.sh
grep -c '10.7.0' Directory.Packages.props || true
git diff --check
```

Results recorded for SWR-G073:

- Full Release build: **0 errors**.
- Full Release test: **188/188 passed**.
- Serialized envelope contract subset: **11/11 passed**.
- Runtime compose E2E: **PASS** — V1 commit/read-back, legacy-unversioned
  acceptance, and both typed rejection cases passed against the 10.8.0 image.
- Remaining `10.7.0` occurrences in `Directory.Packages.props`: **0**.
- `git diff --check`: **clean**.

The compose smoke exercises a V1 commit and read-back through the runtime
container with PostgreSQL, legacy-unversioned acceptance, and typed rejection
of unsupported and case-variant versions. Its generated report is
`reports/smoke/runtime-compose-smoke.md`.

After this PR is merged and all release checks are green, the host/operator may
cut GitHub Release `v1.0.0-preview.3` through the existing protected
`nuget-preview` lane. Creating that release and publishing to NuGet are
explicitly outside this PR.

[#253]: https://github.com/J-Tech-Japan/SekibanWasmRuntime/issues/253
