# WASM materialized-view contract

The runtime host accepts a materialized-view module only after validating one immutable artifact
snapshot. The snapshot is hashed with the full SHA-256 digest, instantiated with Wasmtime, and
queried through `mv_metadata` before the MV services are registered. The executor then instantiates
the validated bytes, so replacing the configured path between validation and execution cannot
switch the module under test.

The versioned boundary is `sekiban-wasm-mv/1`. The only capability currently accepted is
`query-rows`. Metadata and the deployment manifest must converge on the exact set of
`(viewName, viewVersion)` identities and the exact order-insensitive set of logical tables. Empty,
duplicate, missing, extra, malformed, or unsupported identities are rejected before worker,
grain, database, or event work starts. Core modules are hashed as-is; component artifacts are
reduced from the one source snapshot and the extracted core bytes passed to Wasmtime are hashed.
The manifest therefore always carries the digest of the bytes actually instantiated.

## Verified-execution schema decision

The C# guests emit provider-neutral schema metadata and the host maps it to DCB 10.15's
`GetSchemaContract`/`GetSchemaRequirements` contract. Their registered-table contract is therefore
truthful and is verified before the host enters the `VerifyAndExecute` lifecycle.

Rust, Go, TypeScript, Swift, and MoonBit guests currently expose the common identity/ABI/capability
metadata but do not yet expose a provider-neutral schema description. The explicit decision for
this release is **verified-execution deferral** for those guests: `GetSchemaContract` returns
`null`, the empty fallback is treated as `SchemaContractUnavailable`, and the host fails closed
before any lifecycle DML. The production WASM runtime explicitly selects `VerifyAndExecute`: it
verifies host-provisioned schema and registry bindings, then runs projector/checkpoint DML through
the enforced `WasmMvSqlStatementPolicy` without taking CREATE/ALTER/DROP ownership. `CreateOrEnsure`
remains available only to callers that intentionally configure the DCB compatibility path. The
deferral remains until each guest can truthfully describe every registered table, column family,
nullability, key, index, and relevant default/generated expression. No package or release is
published by this repository change.

## Host-owned query policy

The callback context carries the service ID, view identity, and resolved physical bindings. A
callback is read-only, must be a single parameterized `SELECT`, may reference only current-view
physical tables, and is bounded to at most 1,000 rows. Comments, DDL/DML, transaction control,
catalog/framework tables, cross-view references, missing parameters, and invalid row limits are
rejected before the query port is called. Returned initialization/apply statements are sent
through DCB 10.15's enforced `MvPolicyEnforcingQueryPort`/statement-policy path; this repository
does not duplicate that framework statement policy.

The module digest, ABI, capabilities, metadata convergence, real Wasmtime export/metadata call,
path-snapshot behavior, schema decision, and monotonic sortable-unique-id gate are packet-test
inputs. This contract does not weaken the existing SWR-G077 isolation boundary.
