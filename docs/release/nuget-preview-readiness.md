# NuGet Preview 1.0.0-preview.3 Readiness

This is the release-readiness record for the Sekiban.Dcb dependency refreshes
tracked by [#264] and [#268]. The current runtime baseline is 10.14.0 at
dcb-v10.14.0; the historical 10.12.0 evidence remains below for comparison.
This document does not create a GitHub Release or publish packages.

## Dependency Baseline

For SWR-G078, every centrally managed `Sekiban.Dcb.*` package is pinned to
`10.12.0`, and `submodules/Sekiban` is aligned to `dcb-v10.12.0`. The package
and source baselines are intentionally kept together so mixed binary versions
cannot enter one process.

The 10.8.0 notes below are retained as the previous release record.

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

## Serialized Commit Wire-Shape Ground Truth

The contract was measured before changing any emit or accept path. The evidence
came from the actual `Sekiban.Dcb.Core` 10.8.0 package restored from nuget.org,
not from issue wording or the checked-out submodule:

```bash
# `dnx` is the .NET 10 SDK tool runner and downloads the pinned tool package for
# this invocation; no global dotnet-inspect installation is required.
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
name, the per-candidate tag, and the consistency tag. The **wire shape** is
unchanged from 10.7.0: 10.8.0 still uses the V1 envelope, base64 `payload`,
`eventPayloadName`, per-candidate `tags`, and `consistencyTags`. There is no
`events` / `payloadJson` / `eventType` rename and no root-level commit tag list.
The existing envelope black-box tests remain the executable shape guard.

## Tag Reservation Ground Truth

The additive-only API diff does not reveal an important runtime behavior change
inside `GeneralTagConsistentActor.MakeReservationAsync`. The matching
`dcb-v10.8.0` source and package repository commit (`e264a2ed`) changes the
comparison from:

```text
10.7.0: check only when expected and current are both non-empty
10.8.0: normalize null/empty to "" and require expected == current
```

The five 10.8.0 reservation cases are therefore:

| Expected version | Current version | Result |
| --- | --- | --- |
| empty | empty | Accepted: first write. |
| empty | non-empty | Conflict: a first-write request cannot update an existing tag. |
| non-empty | empty | Conflict: the expected tag version does not exist. |
| non-empty mismatch | non-empty | Conflict: optimistic-concurrency failure. |
| non-empty exact match | non-empty | Accepted: existing-tag update. |

This is the behavioral ground truth behind the original CI failure:
`ClientApiCommandFlow` emitted an empty `lastSortableUniqueId` for create,
update, and delete. That was effectively an unchecked update on 10.7.0, but it
correctly fails as an expect-empty request on 10.8.0.

The C# client/sample paths now read the current tag version before update or
delete and copy it into `ConsistencyTagEntry.LastSortableUniqueId`. Create still
uses an empty version, preserving first-write conflict detection. A concurrent
write after the read still fails with the existing optimistic-concurrency
error; the sample does not hide a real conflict with a blind retry.

## Mixed-Version Behavior

The 10.8.0 refresh does not introduce a new envelope version:

| Client → server | Behavior |
| --- | --- |
| Corrected 10.8.0-line client (V1) → 10.8.0-line server | Creates send empty expected; updates/deletes send the exact current version. Accepted when no concurrent write intervenes. |
| 10.7.0-line client (V1) → 10.8.0-line server | Wire shape is readable, but an existing-tag write that sends empty expected now conflicts. Clients already sending the current token remain compatible. |
| Corrected 10.8.0-line client (V1) → 10.7.0-line server | Accepted; 10.7.0 also accepts a matching non-empty expected version. |
| Pre-10.7 client (legacy envelope without `version`) → 10.8.0-line server | Accepted and lifted losslessly to V1. |
| Corrected 10.8.0-line client (V1) → pre-10.7 server | The envelope remains readable by servers that ignore the unknown `version` property; expected-version enforcement depends on that server line. |
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
E2E_SAMPLE=cs bash scripts/e2e-aspire-playwright.sh
bash scripts/smoke-runtime-compose.sh
grep -c '10.7.0' Directory.Packages.props || true
git diff --check
```

Results recorded for SWR-G073:

- Full Release build: **0 errors**.
- Full Release test: **188/188 passed**.
- Serialized envelope contract subset: **11/11 passed**.
- Client expected-version regression subset: **7/7 passed**.
- Aspire + Playwright C# E2E: **PASS** — ClientApi and Web UI create → update
  → delete flows passed; this is the required expected-version gate.
- Runtime compose E2E: **PASS** — V1 commit/read-back, legacy-unversioned
  acceptance, and both typed rejection cases passed against the 10.8.0 image.
- Remaining `10.7.0` occurrences in `Directory.Packages.props`: **0**.
- `git diff --check`: **clean**.

The compose smoke exercises a V1 first commit and read-back through the runtime
container with PostgreSQL, legacy-unversioned acceptance, and typed rejection
of unsupported and case-variant versions. Its generated report is
`reports/smoke/runtime-compose-smoke.md`. It does not replace the Aspire +
Playwright create → update → delete gate, which specifically proves existing-tag
expected-version propagation.

After this PR is merged and all release checks are green, the host/operator may
cut GitHub Release `v1.0.0-preview.3` through the existing protected
`nuget-preview` lane. Creating that release and publishing to NuGet are
explicitly outside this PR.

[#253]: https://github.com/J-Tech-Japan/SekibanWasmRuntime/issues/253
[#264]: https://github.com/J-Tech-Japan/SekibanWasmRuntime/issues/264
[#268]: https://github.com/J-Tech-Japan/SekibanWasmRuntime/issues/268

## SWR-G080 10.14.0 Verification

### Dependency baseline and published API evidence

All 13 centrally managed `Sekiban.Dcb.*` package pins in
`Directory.Packages.props` are now `10.14.0`, and `submodules/Sekiban` is
aligned to tag `dcb-v10.14.0` at commit `a6cd132b`.

The published package surfaces were compared directly with `dotnet-inspect`:

- `Sekiban.Dcb.Core` 10.12.0 → 10.14.0: four additive members/types.
- `Sekiban.Dcb.Core.Model` 10.12.0 → 10.14.0: additive
  `ISortableUniqueIdGenerator` and `MonotonicSortableUniqueIdGenerator`.
- `Sekiban.Dcb.MaterializedView` 10.12.0 → 10.14.0: additive verify-only
  initialization surface; `IMvApplyHost.GetSchemaRequirements` and
  `GetSchemaContract` are default interface members, so existing hosts remain
  source-compatible but do not automatically gain a schema contract.

### Verify-only adoption decision

**Decision: defer verify-only schema-contract adoption to SWR-G079 (#267).**
This slice upgrades the baseline but does not implement
`GetSchemaRequirements`/`GetSchemaContract` in the WASM host. The current
`WasmMvApplyHost` has manifest logical-table names and delegates initialization
and SQL generation to the WASM module; it does not have provider-neutral column,
key, index, type, or generated-column metadata from which a truthful contract
could be built. Inventing that contract here would absorb SWR-G079's validated
WASM contract, digest, metadata, and query-callback policy scope.

The consequence is intentional and explicit: in a table-registering host,
omitting the new schema-requirements methods makes 10.14.0 verify-only
initialization fail closed. Verify-only is therefore not claimed as supported
by this PR; SWR-G079 must provide the validated contract before it is enabled.

### SortableUniqueId ordering finding

The published 10.14.0 `Sekiban.Dcb.Core.Model` implementation routes
`SortableUniqueId.GenerateNew()` through a process-shared
`MonotonicSortableUniqueIdGenerator`. Its logical tick uses an atomic
compare-and-swap floor, so clock rollback and concurrent allocation cannot
make a later generated ID sort before an earlier one. The new
`SortableUniqueIdOrderingTests` regression test generated 256 IDs and verified
strict ordinal increase and `SortableUniqueId.IsEarlierThan` agreement.

This matches the runtime's ordering assumptions. Tag-state/snapshot wait paths
carry the token unchanged and compare persisted `LastSortableUniqueId` with the
requested token using `StringComparison.Ordinal`; the serialized tag-state and
contract suites continue to pass. List-query item ordering remains domain-owned
(`GetWeatherForecastListQuery` orders by `CreatedAt`); the sortable ID is a
consistency/wait boundary, not a replacement for list ordering. No ordering
difference was found.

### Mixed-version behavior and verification

The package pins and source submodule are kept on one 10.14.0 baseline; mixing
10.12.0 and 10.14.0 `Sekiban.Dcb.*` binaries in one process is not supported.
The existing serialized V1 command/query/tag-state contract remains green on
the upgraded baseline. The new verify-only API is additive at the CLR surface,
but a host that registers tables without the schema contract still fails closed
as described above.

Executed gates:

- `dotnet build src/SekibanWasmRuntime.slnx -c Release`: **PASS**, 0 errors
  (143 existing warnings).
- `dotnet test src/SekibanWasmRuntime.slnx -c Release --no-restore`: **PASS**,
  202/202.
- `scripts/contract/run-serialized-dcb-contract-baseline.sh`: **PASS**, 59/59.
- `MaterializedViewServiceIsolationTests`: **PASS**, 2/2.
- `SortableUniqueIdOrderingTests`: **PASS**, 1/1 (included in the full suite).
- `E2E_SAMPLE=cs bash scripts/e2e-aspire-playwright.sh`: **PASS**, 2 passed,
  1 skipped by the existing serialized-command browser fixture.
- `grep -c '10\.12\.0' Directory.Packages.props`: 0.
- `git -C submodules/Sekiban describe --tags`: `dcb-v10.14.0`.
- `git diff --check`: clean.

No tag, GitHub Release, or package publish was performed.

## SWR-G078 10.12.0 Verification

The published 10.12.0 assemblies were used for the compatibility probes. In
the reservation API, `null` means UNOBSERVED/UNSPECIFIED (no comparison), while
`""` means AssertEmpty and a non-empty value means ExactMatch. The serialized
commit contract rejects null; this runtime's normalization to empty is
load-bearing and intentionally preserves serialized safety.

SEK-G22 is authoritative: when the cache is empty but a non-empty expected
version is supplied, 10.12.0 re-reads the event store under the reservation
lock before deciding. This can turn the cached-empty conflict shape into a
successful exact-match update after reconciliation.

The materialized-view catch-up path was exercised by the passing
`reports/smoke/public-container-cs-decider-smoke.md` public-container smoke.
That smoke uses the published `1.0.0-preview.3` runtime image and the 10.2.2
CsDecider fixture, so it proves the published public-container MV surface still
catches up; it does not execute the 10.12.0 MV server code.
The optional `IExecutedUserProvider` remains an opt-in extension point and no
runtime adoption is required by this refresh.

Mixed-version runtime exchange was not executed. Executed evidence is limited
to the same-baseline 10.12.0 serialized contract suite (59/59) and
restore/build/dependency resolution of the preserved 10.2.2/10.1.8 fixtures.
SEK-G22's cached-empty authoritative re-read is a published 10.12.0
source-derived finding, not an observed repo-run result. Package binaries in
one process must remain on one baseline.

The Aspire + Playwright gate ran `weather-clientapi-crud.spec.js` and
`weather-web-ui-crud.spec.js`; `serialized command execute + commit works` was
skipped by the existing browser fixture and is named explicitly here.

## SWR-G077 service-scoped MV adoption

The host adopts Sekiban's released service-scoped MV worker/executor contract
from upstream commits `25c5d8ef` and `c05d4c01`, first released in
`dcb-v10.11.0`; this repository remains entirely on the existing 10.12.0
package baseline. `Sekiban:ServiceId` (or `SEKIBAN_SERVICE_ID`) is normalized
once into an immutable `FixedServiceIdProvider` before MV registration, so the
hosted and Orleans paths share one explicit identity. The host calls
Sekiban's released `AddSekibanDcbMaterializedViewWorkerForService` API with
that identity and does not register the ambient worker. Missing, swapped, or
mismatched identities are rejected before event decode/apply and provider I/O;
the default identity is not an implicit fallback, and single-service
compatibility must be an explicit caller opt-in.

Repository AppHosts set `SEKIBAN_SERVICE_ID=sekiban-wasm-local` for supported
local launches. Direct deployments must set either `Sekiban:ServiceId` or
`SEKIBAN_SERVICE_ID`; missing, empty, invalid, and implicit `default` values
fail during startup and the error names both supported configuration keys.
