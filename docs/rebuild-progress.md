# Rebuild progress - 2026-10-05

This implementation is part of the existing `Osynix.Ats.slnx`. The app and tests remain in their original projects; SDK file inclusion picks up the new pages, services and migrations. [The source analysis](rebuild-analysis.md) records the pre-rebuild baseline, not the current feature status.

## Delivered in this build

| Area | Implemented behavior |
| --- | --- |
| Database | Four versioned EF migrations, recognized legacy-schema baseline, SQLite backup before an existing database upgrade, timestamp-preserving backfills and optimistic concurrency |
| Scoring | All nine original reference cases match, including punctuation matching and 6.05 to 6.1 rounding; rule thresholds are stored and each new result records its policy |
| Clients | Client master, contacts, status/context, duplicate protection and linked position names |
| Positions | Original statuses/priorities, target/contact/recruiter, automatic reference, JD file storage and draft AI summary/rationale, review/locking, duplicate checks, closure history and hired application association |
| Candidate identity | Email, then phone, then unambiguous name; separate people with the same name; stable Candidate ID across applications |
| Assessment history | Same-person/same-reference reassessment creates immutable versions on one current application; lifecycle and original master CV survive; explicit refresh replaces permanent profile/history/CV |
| Profile workspace | Overview, Assessment, Notes and Activity; master CV actions, both summaries, genuine employment/education timelines with legacy-text fallback, selected application and assessment version |
| Lifecycle | Source-controlled lists, client-stage synchronization, interview-round reset, terminal hired protection, original queue precedence and audit before/after changes |
| Talent Pool | Separate route with reusable profile attributes; intentional reassessment from stored source facts without uploading/re-reading the CV |
| Batches | Owner-scoped server records, original file/index mapping, per-item failure and retry, persisted refresh context, Save Remaining and completion; saved items are not re-assessed |
| Reports | Stored version-aware evidence, per-criterion confidence, unavailable historical evidence, comparison page plus individual reports, original-logo configuration, contact exclusion, A4 PDF and page numbers |
| Dashboard | Active-position application scope, lifecycle/funnel/decision totals, client portfolio and matching application/position drilldowns |
| Administration | Admin/Recruiter roles, active/inactive/pending status, source create/talent/export grants, create/edit/password setup, self-disable protection and real audit events |
| Import | Historical criterion JSON, source IDs/dates/statuses, both summaries, structured history, position metadata, settings and 57 source events; existing credentials preserved; source Google tokens excluded |
| UI controls | Correct dynamic Razor bindings, working buttons/messages/decision badges, modal scroll/focus handling, keyboard close and responsive profile layout |

## Verification

- Release solution build passed with **0 warnings and 0 errors**; all **34 tests passed**, with no failures or skips. The expanded suite contains 34 tests, including all original tests and meaningful migration, import, identity, lifecycle, refresh/version, provider, batch, permission, administrator and HTTP workspace/report cases.
- All nine original scoring reference cases now match the Apps Script functions. The original audit JSON intentionally retains the original 7/9 result for historical comparison.
- Actual-workbook rehearsal in an isolated SQLite database: **5 positions, 41 criteria, 42 people, 43 applications, 43 versions, 2 detailed evidence records, 57 source audit events, 6 decision rules**. Both stored criterion evidence records match the original source; Google sessions and trusted-device tokens were excluded.
- Browser QA with a copy of that rehearsal database exercised login, dashboard, profile assessment tab, note creation, keyboard close, position editor and a 390px mobile profile. No browser page errors occurred. Tests used temporary data-protection keys and isolated accounts.
- PDF QA rendered the two real detailed evidence records as one comparison report and reviewed all seven A4 pages, including long tables and hidden-contact behavior. Windows can use installed Edge when bundled Chromium is unavailable. The approved original logo is still a configuration input.
- The actual pre-build backup was restored separately and upgraded successfully. The local application database was backed up and upgraded to all four migrations. Existing Admin sign-in works and its stored password hash is unchanged. Existing position, candidate, assessment and document counts are unchanged. Workbook business data was initially rehearsed separately. At the user's subsequent explicit request, the supplied workbook was imported into the working database after a new consistent backup; see the validation-data update below.
- AI tests use a controlled HTTP provider fixture. **No paid AI request, email, live Google Sheet write or production deployment occurred.**

## Remaining expansion and cutover work

This build restores the operational foundation and workspaces; it does not claim completion of every commercial module in the Word specification.

- Scoped Client/Manager/Admin expansion and client portal: `AssignedClients` and `AssignedReferences` are retained as source metadata, not implemented as a new authorization scope. Current Admin/Recruiter access remains company-wide, with the three enforced grants.
- OTP/email invitations, MFA, request-access flow, trusted-device/session administration and outbound notifications are not implemented. ASP.NET Identity cookies, status validation and password administration are the current authentication flow.
- Automatic talent-to-vacancy recommendations, candidate merge/split tools, advanced search taxonomy controls and large-data server pagination remain future work. Lists currently paginate in memory.
- A fresh export and side-by-side acceptance against the deployed Apps Script app are needed before cutover. Source uploads may differ from the currently deployed version. Import is a one-time migration into an empty business database, not ongoing synchronization.
- Run live JD/CV acceptance using the configured AI model and original documents, review the exact approved report branding, then rehearse final import/backup/restore. Scanned or image-only DOCX input requires a searchable PDF/text alternative.
- SQL Server/PostgreSQL, external object storage, WhatsApp and MCP integrations remain separate extensions.

Start locally with `dotnet run --project src/Osynix.Ats`. Review Settings/rules, import the chosen export if appropriate, then review and lock imported criteria before running new assessments.


## Working validation data loaded

At the user's request, `Appscript files/Osynix_ATS_Master_Tracker.xlsx` is now imported into `src/Osynix.Ats/App_Data/osynix-ats.db`.

- 3 clients, 5 positions, 41 criteria, 42 candidates, 43 applications and 43 original assessment versions.
- 6 decision rules and 57 original activity events; all 43 historical applications' metrics, decisions, populated lifecycle fields and criterion evidence were reconciled with the workbook.
- A consistent `before-workbook-import-...db` backup is in `App_Data/Backups`. Existing Admin credentials and permissions are unchanged; login/password and foreign-key integrity checks passed.
- Populated dashboard, clients, positions, assessment workspace, candidate database, talent pool, pipeline, settings, candidate profile, assessment detail and an original version-1 report loaded successfully through authenticated HTTP checks.
- The Excel source was not modified. No AI requests were needed for import or validation.

Refresh the app to view the loaded data. Imported position criteria remain unlocked for human review; review and lock them before starting new AI assessments. Only two historical applications have original per-criterion JSON evidence; the others retain their overall results and show unavailable detailed evidence. Drive CV/JD links remain links, rather than downloaded file bytes.
