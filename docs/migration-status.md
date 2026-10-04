# Migration status and parity boundaries

Generated 2026-10-04. This is a substantive source implementation, not a tested production release. There was no .NET SDK/compiler in the generation environment and the SDK download endpoint was unreachable. No successful build, live AI request, authenticated UI session, workbook commit, or PDF rendering is claimed.

## Source grounding

Reviewed available uploads from the Osynix ATS folder:

- `OpenAI(4).txt`: evidence-only prompt, exact live criteria names, discrete score/confidence bands, weighted totals, knockout precedence, core-role fit, evidence strength and dynamic decision rules.
- `CandidateService(4).txt`: candidate identity, assessment save, permanent profile fields, lifecycle, CV linkage and talent pool separation.
- `Candidateprofileservice(1).txt`: permanent profiles and position-specific history.
- `positionserive.txt` / `managepositionservice.txt`: JD analysis, position creation, duplicate checks and position listings.
- `Config.txt` / `Codegs(1).txt`: worksheet names and administrator/recruiter routing.
- `reportservice.txt`: candidate reports, scorecard, hidden contact details and original logo requirement.
- Candidate database and navigation HTML/JavaScript uploads were available for UI structure reference. The new UI is a Blazor implementation, not a pixel-identical conversion of those files.

Several uploads have overlapping names and dates. These are source snapshots, not a verified export of every file currently deployed in Apps Script. Final parity must be checked against your current running version.

## Implemented in source

| Area | Behavior |
|---|---|
| UI | Blazor Web App with Interactive Server rendering; responsive white/blue workspace |
| Access | ASP.NET Core Identity, hashed passwords, lockout, Admin/Recruiter roles, authenticated document/report routes, periodic session revalidation |
| Dashboard | Counts and links for active positions, talent pool, shortlist, hires; recruitment-stage distribution |
| Positions | Search, filters, CRUD editing, reviewed/locked criteria, JD AI analysis, close with selected candidate or Client-filled/cancelled reason |
| Criteria | Positive weights totaling 100; duplicate criterion names rejected; no invented seed thresholds |
| Assessment | PDF/DOCX/TXT, 10 MB each, single or batch up to five; strict structured evidence response; deterministic scoring; durable drafts |
| Scoring | Fail → Reject; insufficient knockout evidence → Insufficient Evidence; partial/pass thresholds from stored rules; missing/ambiguous criteria get no credit |
| Candidate database | Search, location/seniority/status filters, 25-row pagination, skills chips, wide details panel, working profile/history/education/assessment/document/edit tabs |
| Save | Transactional talent-profile + assessment association; no silent overwrite of duplicate candidate/reference pair; database uniqueness constraints |
| Lifecycle | Stage, interview, client status, compensation, availability, notice and notes; background-check stage included |
| Reports | Single/batch HTML and optional Chromium PDF; snapshot-based data; original logo configuration; no phone/email output |
| Administration | Decision rules, create Admin/Recruiter accounts and recent audit entries |
| Import | XLSX preview and transactional initial migration; raw row retention; positions, criteria, talent, assessments and M:O decision rules |
| Storage | EF Core / SQLite with foreign keys, WAL, unique indexes, optimistic concurrency tokens and short-lived operation contexts |

## Material differences / unfinished parity work

1. Build and runtime testing are still required. Tests and CI exist, but were not executed here.
2. UI layout is reimplemented, not guaranteed identical to the Apps Script screenshots. Employment and education currently preserve source text and line breaks, rather than a fully structured date-aligned timeline.
3. There is no connection to the live Sheet; no production migration has been performed. The old uploaded workbook was not silently treated as the current source of truth.
4. Historical workbook columns do not guarantee full per-criterion report evidence. Existing scores and summaries are preserved; raw rows remain available for additional mapping. Dates, formulas, hyperlinks and rich formatting need reconciliation against the actual export. The importer stores displayed values, not executable Excel formulas.
5. Imported criteria can be reviewed and repaired before locking; historical imported assessments retain their snapshots. Newly generated locked criteria are immutable.
6. Candidate identity matching deliberately blocks ambiguous names/emails; a manual merge/split UI and reassessment/version replacement flow are not included.
7. PDF output needs Playwright Chromium installation. Exact approved report typography/page-break parity and the original logo must be checked on the target host. The logo itself was not bundled.
8. Original per-user permission flags and all account-management flows are not fully ported. The generated app has two roles. Password reset/email delivery, account disable/role-edit UI, MFA and invitation flows are not included.
9. Lifecycle values are a defined initial set; they are not a complete automatic port of every list and bidirectional synchronization rule in the live workbook. Review these against current operations before cutover.
10. Other saved talent is searchable; automatic candidate-to-new-vacancy fit suggestions are not implemented. No unsupported automated hiring decisions are introduced.
11. Initial schema bootstrap uses `EnsureCreated`; versioned production migrations and a second database provider still need to be established. They are not a one-line production conversion.
12. Lists currently load full small-business data sets and paginate in memory. Larger data sets require server-side filtering/pagination and performance measurement.
13. Dashboard counts are available to Admin and Recruiter; custom recruiter ownership scopes/KPI differences need confirmed rules.
14. WhatsApp and ChatGPT MCP integration remain separate projects and are not included.
15. DOCX extraction uses text paragraphs; scanned DOCX images and DOCX layout require a searchable PDF or text alternative. PDF processing is delegated to the configured model.

## Cutover acceptance checklist

- Build, run automated tests, and exercise login/logout, permissions, persistent drafts and profile tabs.
- Import a fresh workbook into a rehearsal database and reconcile counts, references, names, dates, scores, statuses and original retained rows.
- Compare golden candidate cases against the current Apps Script results, including knockout failure, insufficient evidence, partial evidence and exact decision thresholds.
- Confirm original logo and approve single/batch PDFs, including long evidence tables and hidden phone/email fields.
- Resolve the material differences above that affect your current workflows.
- Back up the live Sheet and CV documents; establish tested database backup/restore and schema migrations.
- Choose a cutover window, freeze writes in the old application, repeat the final import and reconcile before directing recruiters to the new app.
- Keep the existing Apps Script app available for rollback until acceptance is complete.
