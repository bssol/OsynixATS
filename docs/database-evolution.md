# Database evolution

The application uses committed EF Core migrations instead of `EnsureCreated` at startup. New databases run the complete migration chain. Existing original ATS databases are recognized using the real initial migration's table columns, foreign keys and indexes before recording a baseline. Unrecognized schemas fail without recording a guessed baseline.

## Current migration chain

1. `InitialLegacySchema`: exact original starter schema, including Identity and business tables.
2. `RestoreBusinessWorkflows`: clients, structured profiles, application versions, notes, batches, source lists, metadata and nullable historical metrics.
3. `CompleteWorkspaceMetadata`: operational fields needed by workspaces and source evidence.
4. `PersistBatchRefreshContext`: source candidate and explicit refresh mode retained for batch retry.

`DatabaseInitializer` performs a consistent SQLite backup into an adjacent `Backups` folder before upgrading an existing database. It then applies migrations and idempotent backfills without rewriting historical update dates. Backfills normalize identity contacts, retain stable display IDs, map the starter lifecycle vocabulary, preserve current applications as initial immutable versions, link client records and hash existing file bytes. It never resets existing user passwords or imports the workbook automatically.

SQLite table rebuilds can contain operations outside a transaction. Stop other application instances during upgrades, keep the generated backup, and do not interrupt a migration. An unknown/partially upgraded schema should be investigated and restored from backup, not marked applied or silently recreated.

## Upgrade and restore verification

The automated legacy-upgrade test creates the initial schema, seeds users and business records, removes migration history to represent the old `EnsureCreated` database, then upgrades twice. It verifies the existing password hash, business IDs/dates, lifecycle conversion and version backfill. The local working database was also backed up and upgraded with before/after credential and row-count controls.

For future deployment changes:

1. Back up and restore into a separate writable database first using SQLite's backup API (or a fully stopped instance).
2. Upgrade the restored copy with the new application; reconcile users, IDs, documents, versions and record counts.
3. Exercise login, import, reassessment, lifecycle changes and reporting on that copy.
4. Stop the deployed instance, preserve its database and data-protection keys, then start the reviewed version.
5. If an upgrade fails, stop the application and restore its consistent backup; keep the failed database for investigation. Do not delete business data to make startup succeed.

Use an absolute `ConnectionStrings__Ats` path in deployment. Contexts are opened per operation; the SQLite WAL setting is applied during startup. Begin with a single application instance and do not store the database on an unreliable network share.

## Later SQL Server / PostgreSQL move

- Add the selected provider and maintain provider-specific migrations. SQLite migration SQL and PRAGMAs are not portable.
- Transfer Identity, clients/contacts, documents, positions/criteria, candidates/history, applications/versions/batches, notes, settings, audit and import archives in relationship order, preserving IDs and snapshots.
- Validate unique/null semantics, collation, timestamps, binary documents and concurrency with the destination database.
- Use the `IDocumentStore` abstraction if moving binary files to external storage.
- Compare counts/hashes and restore-test before cutover. Changing only a connection string does not implement this move.
