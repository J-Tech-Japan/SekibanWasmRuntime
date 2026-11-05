# WASM materialized-view contract

The runtime host accepts a materialized-view module only after validating one immutable artifact
snapshot. The snapshot is hashed with the full SHA-256 digest, instantiated with Wasmtime, and
queried through `mv_metadata` before the MV services are registered. The executor then instantiates
the validated bytes, so replacing the configured path between validation and execution cannot
switch the module under test.

The versioned boundary is `sekiban-wasm-mv/1`. The only capability currently accepted is
`query-rows`. Rust guests must emit both fields from the WASM `mv_metadata` export, not
only from the deployment manifest: crates.io `sekiban-mv` `0.1.0` omitted
`abiVersion`/`capabilities` in the export payload, so registry consumers must pin
`sekiban-mv = "=0.1.1"` (or newer) after that patch release is published. Metadata and the deployment manifest must converge on the exact set of
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
before any lifecycle DML unless initialization mode is explicitly opted in. The production WASM
runtime default is `VerifyAndExecute` (configuration key
`Sekiban:MaterializedView:InitializationMode`, environment key
`Sekiban__MaterializedView__InitializationMode`): it verifies host-provisioned schema and registry
bindings, then runs projector/checkpoint DML through the enforced `WasmMvSqlStatementPolicy`
without taking CREATE/ALTER/DROP ownership at startup. `CreateOrEnsure` is accepted only through
those keys; `VerifyOnly` and unknown values are rejected at registration. Local dev AppHosts set
`CreateOrEnsure` explicitly: the repository dev AppHost, the Aspire decider template AppHost
(`SekibanWasmRuntimeOptions.EnvironmentVariables`), and both cargo-sekiban generated AppHost
templates. The public `AddSekibanWasmRuntime` helper does not set initialization mode. The runtime
does not provision materialized-view schema at host startup.

Under `CreateOrEnsure`, the host serializes `IMvExecutor.InitializeAsync` in the process so
the catch-up worker and the Orleans grain activator cannot run `CREATE TABLE IF NOT EXISTS`
at the same time. The gate does not issue DDL and does not change who sets the opt-in. It
covers one process only; a second runtime process against the same fresh database can still
race. That cross-process fix is [Sekiban issue 1362](https://github.com/J-Tech-Japan/Sekiban/issues/1362).
When that ships, delete this gate. `VerifyAndExecute` does not install it.

Event-store, Orleans, and the materialized-view worker share one configured ServiceId
(`Sekiban:ServiceId` / `SEKIBAN_SERVICE_ID`). Rows previously written under the implicit ServiceId
`default` (when storage registration overwrote the host provider) are not visible after this
alignment; the configured id (for example `sekiban-wasm-local` on the dev AppHost default) is the
partition key going forward. This repository does not copy or rewrite historical `default` rows.

The deferral remains until each guest can truthfully describe every registered table, column family,
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
