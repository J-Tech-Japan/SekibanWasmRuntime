# Sekiban.Dcb 10.7.0 Baseline and the Serialized Commit Envelope

Records the SWR-G071 move from the Sekiban.Dcb 10.2.2 baseline to 10.7.0, the
serialized commit envelope the remote executor now speaks, and what happens
when a client and a server sit on different versions.

Tracking issues: [#249][i249] (this slice), [#248][i248] (republish + envelope
alignment request, close when the refreshed packages ship),
[Sekiban#1087][s1087] (framework-side republish), [Sekiban#1088][s1088]
(wire-contract confirmation).

## What Changed

- Every `Sekiban.Dcb.*` pin in `Directory.Packages.props` moved from `10.2.2`
  to `10.7.0`.
- The `submodules/Sekiban` checkout moved to the matching `dcb-v10.7.0` tag.
  This is required, not cosmetic: 31 projects `ProjectReference` the submodule
  source while the rest consume the NuGet packages, so the two must agree.
  Bumping only the packages builds clean but fails at runtime with
  `MissingMethodException` on `DcbDomainTypesExtensions.Simple`, because 10.7.0
  adds a third optional parameter (`EventPayloadDeserializationPolicy`) — a
  source-compatible but binary-breaking change.
- `RemoteSekibanExecutor` and `HttpSerializedDcbClient` now emit the **V1**
  serialized commit envelope.
- Every WASM server host (`src/runtime`, `src/internalUsages`, the four
  `src/samples` decider servers) now performs two-phase acceptance instead of
  model-binding the request directly.

## The Envelope: What It Actually Is

The V1 envelope is the legacy official shape **plus an explicit `version`
discriminator**. Nothing was renamed:

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
  "consistencyTags": [{ "tag": "weather:f-1", "lastSortableUniqueId": "0639…" }]
}
```

- `payload` stays a base64-encoded `byte[]`.
- `eventPayloadName` stays.
- `tags` stays **per candidate**. There is no per-commit tags list.

### Why This Contradicts #248 — and Why the Evidence Won

[#248][i248] predicted a rename to `events` / `payloadJson` / `eventType` and a
single per-commit `tags` list, inferred from a `500 ArgumentNullException
(request.Events was null)` seen against a 10.6.0-line server. Implementing that
prediction would have produced an envelope the 10.7.0 deserializer cannot read
(`payloadJson` is a string; `payload` is `byte[]`), breaking the client against
the very server line this slice targets.

Direct measurement of the shipped package contradicted the prediction:

- Reflecting over `Sekiban.Dcb.Core` **10.7.0** finds no commit envelope type
  carrying `events`, `payloadJson`, or `eventType`. The only `PayloadJson` in
  the assembly belongs to `SerializableMultiProjectionState`, and the only
  `EventType` to `ProjectionFaultDescriptor` — neither is on this path.
- Serializing `VersionedSerializedCommitRequest` through the framework's own
  `SerializedCommitWireContract.Options` emits exactly the shape above.
- The framework source agrees. `LegacyUnversionedSerializedCommitAdapter`
  states that "the official contract never changed across dcb-v10.2.2 →
  10.6.0" and that the lift "NEVER passes through a per-commit-tag model (that
  shape belongs to a downstream runtime contract, not this one)".

What 10.7.0 actually introduced (`dcb-v10.7.0` = `ed9ce341`, "SEK-G17 —
serialized-commit wire contract hardening") is **envelope versioning**:
`VersionedSerializedCommitRequest` (`CurrentVersion = 1`),
`SerializedCommitVersionDiscriminator`,
`LegacyUnversionedSerializedCommitAdapter`, `SerializedCommitAcceptor`, and the
typed failures `MalformedSerializedCommitException` /
`UnsupportedSerializedCommitEnvelopeVersionException`.

Because [Sekiban#1088][s1088] is still open, the per-commit tag collapse it
would have required is deliberately **not** implemented here. Per-candidate
tags are preserved verbatim and pinned by test. If #1088 later confirms a
per-commit shape, that is a separate slice.

## Mixed-Version Behavior

Acceptance is two-phase: the raw `version` discriminator is read straight from
the request bytes **before** any typed binding, base64 decode, tag reservation,
EventId allocation, or executor call. Off-contract envelopes therefore fail
before anything is written.

| Client → Server | Result |
| --- | --- |
| 10.7.x client (V1, `version: 1`) → 10.7.x server | Accepted. The primary path. |
| 10.2.2-era client (no `version`) → 10.7.x server | Accepted. Discriminated as the legacy unversioned official shape and lifted losslessly to V1; per-candidate tags preserved. |
| 10.7.x client (V1) → pre-10.7 server | Accepted. Servers predating the discriminator ignore the unknown `version` property and bind the same candidates. |
| Any client sending an unsupported `version` (e.g. `999`) | Rejected: HTTP 400, code `unsupported_commit_envelope_version`. |
| Case-variant (`Version`), duplicated, or non-integer `version`; non-object root; unbindable payload | Rejected: HTTP 400, code `malformed_commit_envelope`. Matching is deliberately case-sensitive so an off-contract casing is never read optimistically. |

Rejection messages never echo request content, so hostile payloads cannot leak
through the error surface.

## Next Release Expectation

`Sekiban.Dcb.WasmRuntime.Remote 1.0.0-preview.1` immutably depends on
`Sekiban.Dcb.WithoutResult[.Model] 10.2.2`, which is what left
[SekibanAsAService][saas1768] holding a 10.2.2 compatibility island. The next
publish (**1.0.0-preview.2** or later) must carry the `10.7.0` dependency range
produced by this slice.

Publishing is out of scope here. It goes through the existing lane — GitHub
Release trigger, tag defines the version, `nuget-preview` protected environment
— see [`nuget-preview-release.md`](nuget-preview-release.md). Close [#248][i248]
once those refreshed packages ship.

## Evidence

- `dotnet build src/SekibanWasmRuntime.slnx -c Release` — 0 errors.
- `dotnet test src/SekibanWasmRuntime.slnx -c Release` — 188/188 passed.
- Contract tests: `RemoteSekibanExecutorTests.ExecuteAsync_ShouldEmitVersion1SerializedCommitEnvelope`
  pins the emitted shape and asserts `events` / `payloadJson` / `eventType` /
  root-level `tags` are absent; `SerializedCommitEnvelopeContractTests` covers
  V1 acceptance, legacy lift, per-candidate tag preservation, and each
  fail-closed path.
- End-to-end against the runtime container (`scripts/smoke-runtime-compose.sh`,
  external Postgres): V1 commit → read-back via `tag-latest-sortable`; legacy
  unversioned commit accepted; `version: 999` and `Version` both rejected with
  HTTP 400. Report: `reports/smoke/runtime-compose-smoke.md`.

[i248]: https://github.com/J-Tech-Japan/SekibanWasmRuntime/issues/248
[i249]: https://github.com/J-Tech-Japan/SekibanWasmRuntime/issues/249
[s1087]: https://github.com/J-Tech-Japan/Sekiban/issues/1087
[s1088]: https://github.com/J-Tech-Japan/Sekiban/issues/1088
[saas1768]: https://github.com/J-Tech-Japan/SekibanAsAService/issues/1768
