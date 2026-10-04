# Osynix ATS — .NET 10 / Blazor Interactive Server / SQLite

A generated migration implementation based on the available Apps Script source uploads. All application code is under `src/Osynix.Ats`. It uses real SQLite persistence, server-side Blazor components, ASP.NET Core Identity and an OpenAI Responses API adapter.

**Delivery status:** source generated and statically reviewed; NOT compiled or run in the generation environment because the .NET SDK is absent and the SDK download was unreachable. CI and automated tests are included but have not executed. This is not a verified production replacement or a claim of complete visual/behavioral parity. Read `docs/migration-status.md` for differences and acceptance gates. The Apps Script app and live Google Sheet have not been modified. No production records or secrets are included.

## 1. Prerequisites

- .NET 10 SDK (10.0.100 or a later .NET 10 feature band).
- Internet access for NuGet restore and, when enabled, OpenAI API requests.
- Your latest Google Sheets export as `.xlsx` for data migration.

## 2. Verify the source

From the extracted project root:

```powershell
./tools/verify.ps1
```

Linux/macOS alternative:

```bash
./tools/verify.sh
```

These scripts restore dependencies, build all projects and run the deterministic-scoring, storage and import tests. If restore fails, verify access to NuGet; if compilation fails, retain the exact build output for correction before deploying.

## 3. Configure the first administrator

Use development user secrets rather than committing passwords:

```powershell
dotnet user-secrets set "Bootstrap:Email" "admin@your-domain.com" --project src/Osynix.Ats
dotnet user-secrets set "Bootstrap:Password" "YOUR-UNIQUE-STRONG-PASSWORD" --project src/Osynix.Ats
```

Use at least 12 characters, upper/lowercase, numbers and a symbol. The bootstrap account is created only when the user table is empty. No default account or password is embedded. Remove the bootstrap password from configuration after first sign-in.

## 4. Optional AI setup

```powershell
dotnet user-secrets set "OpenAI:ApiKey" "YOUR-API-KEY" --project src/Osynix.Ats
dotnet user-secrets set "OpenAI:Model" "YOUR-ENABLED-RESPONSES-MODEL" --project src/Osynix.Ats
```

Choose a model your API project can access that supports strict JSON-schema outputs and PDF file input. The old source configured `gpt-5.6`; it is not copied as an assumed available API model. Configure your actual API model ID explicitly. The API key stays on the server. No AI calls occur on page load, import, profile edits or report generation. Clicking **Analyze JD with AI** or **Run assessment** sends the relevant document data to OpenAI and incurs API usage costs. ChatGPT subscription billing is separate from this application.

No fake results or fallback scores are returned when configuration is missing, the provider fails, or the response is incomplete. Batch processing is sequential, at most five CVs; completed items remain saved as drafts if another item fails. Retrying a provider operation can incur another charge.

## 5. Run locally

```powershell
dotnet run --project src/Osynix.Ats
```

Open `http://localhost:5080`, sign in, and use **Import workbook** for migration or **Positions** to create a vacancy. SQLite is initialized at `src/Osynix.Ats/App_Data/osynix-ats.db` when running from the project. The default database is empty, with no fabricated candidates or decision thresholds. Set approved rules in Settings or import them from `Lists & Settings` columns M:O. Review and lock criteria before screening.

## 6. Reports and original logo

Reports hide candidate email and phone and preserve image aspect ratio. Configure a local **unchanged original logo**:

```powershell
dotnet user-secrets set "Branding:LogoPath" "C:/Osynix/branding/original-logo.png" --project src/Osynix.Ats
```

Without the original logo, reports are explicitly marked **Draft report — original logo not configured**. No replacement logo has been invented.

HTML print previews work without a browser installation. For direct PDF downloads:

```powershell
dotnet build src/Osynix.Ats
dotnet run --project src/Osynix.Ats -- --install-browser
```

The browser installation command includes system dependency installation; on Linux this may require administrator privileges. Install under the same OS account used to run the app, or configure a shared `PLAYWRIGHT_BROWSERS_PATH`. This Chromium requirement applies to server PDF rendering, not users' browsers. The provided Dockerfile installs Chromium during image build. That image build also requires access to Microsoft images, package repositories and Playwright downloads.

## 7. Import the workbook

1. Export the **current** live Google Sheet via File → Download → Microsoft Excel (.xlsx).
2. Start with a fresh ATS database; use a separate copy for rehearsal.
3. As administrator, open **Import workbook**, choose the file, review sheet counts and warnings, then commit.
4. Import runs transactionally. Duplicate names, duplicate candidate/reference pairs or missing position relationships cause the whole operation to roll back.
5. Compare positions, profiles, decisions and stage totals with the workbook. Review criteria and lock them explicitly. Review decision rules before running new assessments.
6. Original imported rows are retained in `ImportRows`, including unmapped columns. CV Drive links are retained; file bytes are not automatically fetched. Uploads for new assessments are stored in SQLite.

The importer supports `Positions Master`, `JD Criteria`, `Talent Pool Master`, `Candidates` (or `Recruitment Pipeline`) and decision rules in `Lists & Settings` M:O. It is an **initial migration** importer, not an ongoing synchronization tool. See `docs/data-mapping.md` for exact mappings and limitations.

## 8. Production configuration

Use host environment variables or a secret manager:

- `ConnectionStrings__Ats` — SQLite connection string with an absolute writable file path.
- `OpenAI__ApiKey`, `OpenAI__Model` — provider settings.
- `Bootstrap__Email`, `Bootstrap__Password` — first administrator only.
- `Branding__LogoPath` — original logo file path.
- `AllowedHosts` — your actual deployment hostname.

Use HTTPS. Persist the database directory and ASP.NET Core Data Protection keys across restarts. SQLite deployment should initially use one application instance; do not place the database on an unreliable network share. Back up the database consistently, including WAL state or with SQLite's backup API. Never copy just the main `.db` while active writes are happening. Restore-test before cutover.

Schema creation currently uses `EnsureCreated` for the initial version. It does **not** automatically upgrade an existing schema. Read `docs/database-evolution.md` before changing the data model or deploying upgrades.

## Project layout

```text
src/Osynix.Ats/
  Components/       Blazor pages, shared components and layout
  Data/             Identity + EF Core context
  Domain/           Entities, snapshots and deterministic scoring
  Services/         ATS operations, OpenAI, import, reports and access control
  Pages/Account/    Cookie-based sign-in/sign-out
  wwwroot/          Responsive application styles
tests/              Scoring, persistence and migration tests
docs/               Migration status, mappings and database evolution
tools/              Build/test commands
.github/workflows/  Build and test workflow for GitHub
```

## References used

- [Blazor render modes (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0)
- [EF Core SQLite provider](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/)
- [SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)
- [EF migrations with multiple providers](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)
