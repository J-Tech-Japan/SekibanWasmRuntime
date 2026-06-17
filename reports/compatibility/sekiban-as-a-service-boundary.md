# SWR-G008 Compatibility Boundary Evidence

Issue: `SWR-G008 SekibanAsAService compatibility contract boundary`

## Result

The runtime/service boundary is documented in
`docs/compatibility/sekiban-as-a-service-boundary.md`.

The documented contract keeps SekibanWasmRuntime responsible for:

- Generic serialized DCB DTOs and endpoint semantics.
- `ISerializedDcbClient`, `ISerializedQueryClient`, `ISekibanWasmExecutor`,
  and command request builder abstractions.
- Runtime-side black-box scenarios for command commit, tag state, query, and
  C# / Rust WASM compatibility.

SekibanAsAService remains responsible for:

- Real provider configuration.
- Credential and authorization flow.
- Hosted client wrappers.
- Product-specific lifecycle and support policy.

## Evidence Matrix

| Requirement | Runtime-owned evidence | Downstream evidence |
| --- | --- | --- |
| Command commit compatibility | `HttpSerializedDcbClientTests`, `SerializedCommandEndpointContractTests`, `SerializedCommandEndpointsExecuteTests`, `RemoteSekibanExecutorTests`. | Execute the same command commit path through the real hosted provider. |
| Tag state compatibility | `HttpSerializedDcbClientTests`, `InProcSerializedDcbClientTests`, `WasmTagStatePrimitiveTests`. | Read tag state through the real hosted provider and credential path. |
| Query compatibility | `HttpSerializedQueryClientTests`, `RemoteSekibanExecutorTests`, `WeatherQueryClientTests`. | Run scalar and list queries through the real hosted client. |
| Runtime host compatibility | `dotnet test src/SekibanWasmRuntime.ci.slnx`, `./build/scripts/run-e2e.sh`, and primary sample WASM builds. | Confirm the hosted provider accepts the same serialized DTOs and endpoint semantics. |
| No service-specific implementation leak | `rg -n "SekibanAsAService|credential|hosted client|provider binding" src/lib` during review. | Keep provider, credential, and hosted client implementation in the service repository. |

## Review Notes

This issue did not require a new service client or provider implementation in
`src/lib`. The accepted change is a contract boundary plus evidence mapping so
the downstream service can depend on the runtime-owned contract without leaking
service-specific implementation concerns into public runtime packages.
