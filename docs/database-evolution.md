# Database evolution

SQLite is already a relational database. The intended later phase is migration to a server RDBMS such as SQL Server or PostgreSQL.

The domain and application services use EF Core. SQLite-specific setup is isolated to the provider registration and initial WAL setting in `Program.cs`. Connections/contexts are opened per operation rather than kept alive through the Blazor circuit. Files currently live in the database as byte arrays; move them behind object-storage services if the volume warrants it.

## Initial version

The generated application uses `EnsureCreatedAsync` to bootstrap an empty SQLite database. This is a documented initial-development limitation, not a production migration strategy. `EnsureCreated` does not update existing tables when model classes change.

Before the first production deployment, on a machine with the .NET 10 SDK:

1. Work on a disposable development database with a backup of any imported data.
2. Install a compatible `dotnet-ef` 10.x tool and scaffold an initial migration with `dotnet ef migrations add InitialCreate --project src/Osynix.Ats`.
3. Replace `EnsureCreatedAsync` with your controlled migrations deployment strategy (`MigrateAsync` only where deployment policies permit it), not both.
4. Recreate the disposable database with that migration and repeat the import. Do not run an initial create migration against an existing EnsureCreated database; it will try to create existing tables.
5. Commit the migration and model snapshot. Test upgrade and restore procedures on a copy before future schema changes.

For a deployed EnsureCreated database that cannot be recreated, plan and test an explicit baseline process. Do not delete production data or mark unexecuted migrations as applied blindly.

## Later SQL Server / PostgreSQL move

- Add the selected EF Core provider with a version compatible with EF Core 10.
- Choose the provider at startup through configuration. Run SQLite PRAGMAs only for SQLite.
- Maintain separate provider-specific migration sets; EF migration SQL is not portable across databases.
- Transfer data in foreign-key order (Identity, positions, criteria, documents, candidates, assessments, settings, audits and import rows), preserving GUIDs and snapshots.
- Validate null semantics, unique indexes, collation/case behavior, string limits, UTC dates, binary files and concurrency handling.
- Compare counts and hashes and test an actual rollback plan. Changing only the connection string is not sufficient.

Microsoft references:
https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations
https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers
