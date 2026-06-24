# Runtime Host Postgres Schema Smoke (SWR-G041)

Classification and fix for the public-container sample smoke failure where the
runtime reaches `/health` but the first commit fails with:

```text
Npgsql.PostgresException: 42P01: relation "dcb_events" does not exist
```

## Reproduction

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/smoke.sh
# On Apple Silicon, the current public preview 1 image is amd64-only, so reproduce with
#   DOCKER_DEFAULT_PLATFORM=linux/amd64
# (that override is only for the amd64-only preview 1 tag, not a preview 2 completion condition).
```

Symptom: the Aspire AppHost creates an empty Postgres database `SekibanDcb`, the
runtime container connects and serves `/health` (and `/ready`, pre-fix), but the
first `POST /api/sekiban/serialized/commit` fails because the `dcb_events` table
was never created.

## Root cause (classified)

The failure is a combination of an upstream default and a runtime-host gap:

1. **Upstream `Sekiban.Dcb.Postgres` (consumed as a NuGet package).**
   `AddSekibanDcbPostgresWithAspire(...)` registers a `DatabaseInitializerService`
   hosted service whose `StartAsync` does `CanConnectAsync()` then
   `EnsureCreatedAsync()`. Aspire **pre-creates the `SekibanDcb` database empty**,
   so `EnsureCreatedAsync()` sees the database already exists and **no-ops**
   (EF Core only creates tables when it creates the database), then logs
   "tables already exist" — falsely. No `dcb_events` table is created.

2. **Runtime host (this repo) — the fixable gap.** The host already has a
   reliable path: `Program.cs` calls `app.MigrateSekibanDcbDatabaseAsync()` (EF
   `MigrateAsync`, which applies the `Initial` migration that creates
   `dcb_events` even on an existing empty database) — **but only when**
   `RuntimeHostStorageConfiguration.RequiresRelationalMigration` is true, which
   was scoped to **Sqlite only**. Postgres skipped the migration entirely.

So Postgres relied solely on the upstream `EnsureCreated` no-op, and the schema
was never created.

## Fix (in this repo)

- `RuntimeHostStorageConfiguration.RequiresRelationalMigration` now includes
  **Postgres** (both relational providers migrate at startup). The host runs EF
  migrations for Postgres, reliably creating `dcb_events` before serving traffic.
  This does not depend on the upstream hosted service.
- `/ready` is now **schema-aware and fail-closed** for Postgres: it probes the
  `dcb_events` table (not just `CanConnect`), returning `503` with
  `dcb schema missing: ...` when the schema is absent.
- The public-container smoke now **gates on `/ready`** (schema-aware), uses a
  clean curl config (`curl -q`, so a user `~/.curlrc` can't break it), and on
  failure captures the HTTP response body and the runtime container logs. It
  fails closed when the schema is missing instead of reporting a misleading pass.

## Required follow-ups

- **Upstream `Sekiban.Dcb.Postgres` (NuGet) — recommended, not required for this
  repo's fix.** `DatabaseInitializerService` should not depend on
  `EnsureCreatedAsync()` for an Aspire-pre-created empty database (it silently
  skips schema creation and logs success). It should run migrations or check
  `HasTables` before assuming the schema exists. Track as an upstream
  `Sekiban.Dcb.Postgres` package change; until then, consumers must run
  `MigrateSekibanDcbDatabaseAsync()` (as this runtime host now does for Postgres).
- **Republished runtime-host container (preview 2 ordering).** The published
  preview 1 image (`1.0.0-preview.1`) predates this fix and still fails the fresh
  Postgres commit. A **republished runtime-host container is required** before
  preview 2 can claim a green commit/query smoke. See
  [`runtime-host-preview-2-release-checklist.md`](runtime-host-preview-2-release-checklist.md):
  do not claim preview 2 readiness until the commit/query smoke is green against
  the preview 2 image.
