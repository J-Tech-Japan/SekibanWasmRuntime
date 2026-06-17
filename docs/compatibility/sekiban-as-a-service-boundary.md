# SekibanAsAService Compatibility Boundary

This repository owns the generic SekibanWasmRuntime compatibility contract. It
does not own service-specific provider bindings, credential helpers, or hosted
client implementations.

The boundary is intentionally package-level and endpoint-level: applications can
depend on the public runtime packages and serialized DCB HTTP shape without
pulling service-specific implementation concerns into `src/lib`.

## Runtime-Owned Contract

SekibanWasmRuntime owns these public runtime surfaces:

| Surface | Runtime responsibility | Compatibility evidence |
| --- | --- | --- |
| `Sekiban.Dcb.WasmRuntime` | Shared projection abstractions, serialized command/query DTOs, `ISerializedDcbClient`, `ISerializedQueryClient`, `ISekibanWasmExecutor`, and `ISekibanCommandCommitRequestBuilder`. | Unit and contract tests in `src/internalUsages/cs/SekibanWasm.Cs.Tests`. |
| `Sekiban.Dcb.WasmRuntime.Remote` | Generic HTTP clients for the serialized DCB endpoints and remote executor support. | HTTP client endpoint tests and remote executor tests. |
| `Sekiban.Dcb.WasmRuntime.Wasmtime` | In-process WASM projection hosting and tag-state projection runtime. | Runtime host tests plus C# and Rust WASM sample flows. |
| Sample runtime topologies | Black-box examples that exercise command commit, tag state, query, and runtime host compatibility. | `dotnet test src/SekibanWasmRuntime.ci.slnx` and `./build/scripts/run-e2e.sh`. |

The generic serialized DCB boundary consists of:

- Tag state reads through `ISerializedDcbClient.GetSerializableTagStateAsync`
  and `POST /api/sekiban/serialized/tag-state`.
- Serialized event commits through
  `ISerializedDcbClient.CommitSerializableEventsAsync` and
  `POST /api/sekiban/serialized/commit`.
- Serialized command execution through
  `ISerializedDcbClient.ExecuteSerializedCommandAsync` and
  `POST /api/sekiban/serialized/command/execute`.
- Serialized scalar and list queries through `ISerializedQueryClient` and
  `POST /api/sekiban/serialized/query` or
  `POST /api/sekiban/serialized/list-query`.
- Higher-level executor calls through `ISekibanWasmExecutor`, backed by
  transport-neutral command request building and serialized query clients.

These contracts are generic runtime contracts. They must not require
SekibanAsAService credentials, tenant resolution, provider-specific storage
configuration, or service-specific client code.

## SekibanAsAService-Owned Contract

SekibanAsAService owns compatibility above the generic runtime boundary:

| Surface | Downstream responsibility |
| --- | --- |
| Real provider configuration | Prove the hosted provider can satisfy the same serialized DCB endpoint contract. |
| Credential flow | Attach authentication and authorization outside the runtime packages. |
| Hosted client implementation | Wrap the generic runtime clients without changing their DTOs or endpoint semantics. |
| Product lifecycle policy | Decide service-specific availability, rollout, and support guarantees. |

The downstream service should treat SekibanWasmRuntime as a black-box runtime
dependency. Its tests should prove that real provider and credential flows still
pass the serialized command, tag-state, and query scenarios listed below.

## Black-Box Compatibility Scenarios

Runtime-owned black-box scenarios are testable without service-specific
credentials:

| Scenario | Contract under test | Runtime-side expected result |
| --- | --- | --- |
| Command commit | Execute a command, build serialized event candidates, commit with consistency tags. | Commit returns written events and tag write results, and the latest sortable unique ID is preserved. |
| Tag state | Read an existing tag state by serialized tag-state ID. | Response contains payload bytes, tag group/content/projector, version, projector version, and last sortable unique ID. |
| Scalar query | Execute a serialized query request. | Response payload JSON deserializes to the requested result type. |
| List query | Execute a serialized list query request. | Response contains items JSON plus paging metadata. |
| Runtime compatibility | Run the same observable command/query/tag-state flow through supported WASM modules. | C# and Rust primary sample flows remain compatible with the public runtime contracts. |
| Boundary guard | Use the runtime packages without service-specific credentials or provider helpers. | `src/lib` remains free of SekibanAsAService implementation code. |

SekibanAsAService-owned tests should repeat the command commit, tag state, and
query scenarios against its real provider and credential path. Failures in those
tests are downstream provider/client compatibility failures unless they expose a
generic runtime contract bug in this repository.

## Evidence Commands

Use these commands when reviewing runtime-side compatibility:

```bash
dotnet test src/SekibanWasmRuntime.ci.slnx
./build/scripts/run-e2e.sh
./scripts/build-samples-wasm.sh --primary
```

For targeted contract review, focus on:

- `HttpSerializedDcbClientTests` for `tag-state`, `commit`, and
  `command/execute` endpoint shape.
- `HttpSerializedQueryClientTests` for `query` and `list-query` endpoint shape.
- `SerializedCommandEndpointContractTests` and
  `SerializedCommandEndpointsExecuteTests` for command DTO serialization and
  command-to-commit mapping.
- `RemoteSekibanExecutorTests` for the higher-level remote executor flow.

Runtime package changes that alter any DTO, endpoint, or executor behavior
should update these tests and this boundary document in the same pull request.

## Non-Goals

SekibanWasmRuntime does not implement:

- SekibanAsAService provider bindings.
- Credential acquisition, token refresh, or authorization policy.
- Service-specific hosted client wrappers.
- Service branding or hosted product documentation.

Those concerns belong in the downstream service repository. This repository only
documents the generic runtime contract those integrations can depend on.
