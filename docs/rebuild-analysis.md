> Historical baseline: this analysis describes the starter before the rebuild. For the implementation and current checks, see [Rebuild progress](rebuild-progress.md).

# Osynix ATS source analysis and rebuild plan

Reviewed 5 October 2026 against the files currently in `Appscript files`, the Word migration specification, the supplied tracker workbook, and the implementation in `src/Osynix.Ats`.

The Blazor application is a working foundation, but several original business rules and important parts of the approved target product are absent or changed. Restoring functionality requires a fuller domain model and source-based acceptance tests, followed by rebuilding the recruiter workspaces. Adding screens to the current model alone would leave identity, reassessment, lifecycle, evidence and history problems unresolved.

This document records the analysis and a concrete implementation sequence. It does not claim that the missing modules have been implemented. The production SQLite database and its administrator account were not changed during this review. Workbook import was exercised only against an isolated, in-memory SQLite database.

## 1. Evidence and scope

The supplied folder contains 39 files: 37 Apps Script, HTML, JavaScript, style or configuration text files, one XLSX and one DOCX. Inventory identified 419 named JavaScript function declarations, including frontend helpers; that number is not a count of backend endpoints. The Word specification has 371 paragraphs and 13 tables. The workbook has 13 worksheets and 14,175 formula cells, many on preallocated empty rows.

The source inventory, hashes, workbook headers, validations and executable scoring comparison are recorded in [source-audit-2026-10-05.json](source-audit-2026-10-05.json). Synthetic input cases are in [scoring-reference-cases.json](scoring-reference-cases.json).

Evidence has three different meanings:

- **Reference behavior:** an operation implemented in the supplied Apps Script/HTML or a calculation present in the workbook.
- **Approved target:** behavior explicitly approved in the Word document but identified there as pending or commercial-product scope.
- **Source gap:** a missing implementation or inconsistency in the supplied originals. This is not evidence that the currently deployed Google application has the same defect.

Use the specification's explicit business decisions as the target, validate existing behavior against the actual script and workbook, and record conflicts rather than silently choosing whichever file is easiest to copy. The supplied folder is an offline snapshot; this analysis does not verify the live Google deployment, Drive file availability, email delivery, model access or rendered PDF parity.

The earlier [source manifest](source-manifest.json) refers to differently named snapshots. Keep it as historical provenance. The new inventory identifies the exact files reviewed now.

## 2. Actual workbook and migration findings

### Workbook responsibilities

| Worksheet | Actual populated records or role | Treatment in .NET |
| --- | --- | --- |
| Lists & Settings | Controlled values and configurable decision rules | Typed reference data and versioned scoring policy |
| Positions Master | 5 vacancies; 30 columns | Positions, clients, assignments, JD documents and closure history |
| JD Criteria | 41 criteria: 7, 9, 8, 8 and 9 across the five references | Approved criterion sets, preserved weights and interpretation |
| Talent Pool Master | 42 permanent people; 29 columns | Candidate master, structured profile and candidate-level CV |
| Candidates | 43 applications belonging to 42 people; 28 columns | Applications, assessment versions, criterion evidence and lifecycle |
| Recruitment Pipeline | 11 stage aggregation rows | Query/view; this sheet is not an application table in this workbook |
| Dashboard | Position selection and talent/position calculations | Query/view |
| New Dashboard | Dashboard presentation and Google query output | Query/view |
| Dashboard Data | KPI, funnel and decision aggregates | Query/view |
| Users & Access | 2 user records, status, assignments and permission flags | Identity profiles, grants and assignments; not password recovery |
| Activity Log | 57 real events | Imported historical events with original actor and timestamp |
| User Sessions | 41 populated session rows | Replace with .NET session behavior; do not reactivate Google sessions |
| Trusted Devices | 4 populated device records | Replace with the chosen .NET authentication behavior |

The repeated Candidate ID across two application rows is expected: one person has two vacancy applications. Counts must reconcile by Candidate ID and Candidate ID + Ref #, not by assuming one application per person or using display names as identities.

### Executed import rehearsal

The current importer successfully imported **5 positions, 42 candidates and 43 assessments** into an in-memory database. The six Pass/Partial decision thresholds were correctly read from formatted percentage cells. No live data was imported.

However, a successful count reconciliation is insufficient:

1. **Criterion evidence is omitted.** `Candidates!AB` has valid criterion JSON for two applications. `WorkbookImport.cs:92` always creates a snapshot with an empty criteria collection; both imported records therefore have zero operational criterion evidence. The raw row archive retains the JSON, so recovery is possible, but the profile/report cannot use it.
2. **Historical dates are not mapped.** All 43 Date Screened values are real workbook dates. Entity timestamps default to the import time; original screening, last-assessed, last-updated, open and closure dates are not mapped to their operational meanings. Source chronology cannot be replaced by import order.
3. **Identity matching uses names.** The workbook supplies Candidate ID, but the importer resolves application relationships by normalized name and rejects identical names. This can merge or reject legitimate different people in other exports.
4. **Structured history is stored as text.** One profile contains structured employment JSON and one structured education JSON. Three other profiles contain legacy text in each field. Both formats must render correctly; the remaining 38 empty fields must stay empty.
5. **Position management metadata is archived rather than mapped.** Priority, hiring target, hiring contact, assigned recruiter, JD link/hash, created-by/date and closure fields need operational fields and relationships.
6. **The two profile summaries are collapsed.** Professional Summary and Career Profile Summary are separate source fields. The target model has only `Summary`, and import chooses one value.
7. **Controlled lists are not migrated.** Only decision rules are imported. Original recruitment/interview/client/talent/seniority lists do not become the application's validated choices.
8. **Audit and access data remain raw rows.** Existing event timestamps/actors, user status, assignments and grants are not mapped into their .NET modules.
9. **Google formula wrappers are not a portable calculation engine.** There are 6,126 cells containing `__xludf.DUMMYFUNCTION`, including exports of FILTER, UNIQUE, TEXTJOIN, QUERY and computed values. Their fallback/cached values are snapshot data, not proof that Excel or ClosedXML can recalculate the original Google logic.

Keep the original workbook as migration evidence. Import supported values and histories, then reproduce operational calculations in application services. Do not import derived dashboard grids as business entities. Retain unknown historical metrics as null/unavailable rather than converting them into invented zero scores.

## 3. Functional gap matrix

Priorities indicate dependency and impact. **P0** means data preservation or outcome correctness; **P1** means core recruiter functionality; **P2** means approved expansion or additional administration.

| ID | Priority | Module | Reference or approved requirement | Current implementation and gap |
| --- | --- | --- | --- | --- |
| G01 | P0 | Historical import | Preserve stored criterion JSON and real historical dates; never reconstruct missing evidence | Counts import correctly, but criterion JSON and operational dates are omitted |
| G02 | P0 | Candidate identity | Email first, then normalized phone, then an unambiguous normalized name | `SaveDraft` unions email/name matches, does not use phone, and can block an email-resolved person because another name matches |
| G03 | P0 | Same-reference assessment | Same person + same Ref updates the existing application and preserves recruiter progress | Save is rejected when that person/reference already exists; there is no assessment-version workflow |
| G04 | P0 | Lifecycle lists | Original stage, interview and client controlled values | Many original values are unavailable; replacements such as Interviewed, Selected and Shared change meaning |
| G05 | P0 | Lifecycle synchronization | Client outcomes take precedence; offer and client-interview rules; completed interview means pending decision | Only three stage-to-status assignments exist; original client-to-stage synchronization is absent |
| G06 | P0 | Scoring normalization | Criterion matching normalizes punctuation as in `normalizeCriterionName_` | Whitespace/case normalization only; verified outcome divergence |
| G07 | P0 | Scoring rounding | JavaScript's explicit one-decimal rounding | Default .NET midpoint rounding differs; verified 6.1 versus 6.0 Core Role Fit |
| G08 | P0 | Database evolution | Add modules without deleting existing identities/data | `EnsureCreated` has no upgrade path when schema changes |
| G09 | P1 | Candidate master | Human-readable Candidate ID, separate summaries, structured employment/education | New candidates have no generated display ID; summaries collapse and history is text |
| G10 | P1 | Latest employment | Current/Present record first, otherwise latest documented end date; top-level fields agree with history | No structured employment schema, reconciliation or deterministic fallback |
| G11 | P1 | Candidate Database | Operational person/application context, lifecycle queues and candidate profile | Current `/candidates` is largely a talent repository; application controls live in a separate pipeline page |
| G12 | P1 | Candidate Profile | Overview, Assessment, Notes, Activity; header CV action; 65/35 overview and real timelines | Different six-tab layout; assessments navigate away; no Notes/Activity tabs or structured timelines |
| G13 | P1 | Talent Pool | Separate sourcing/reuse workspace, independent of one application's rejection | Candidate master exists, but there is no separate Talent Pool route/workflow |
| G14 | P1 | Manage Positions | Priority, hiring target, client/status/priority filters, sorting/pagination, position metrics/context | Basic title/client/reference/status listing; missing target, priority, contact, assignments and application metrics |
| G15 | P1 | JD workflow | Upload JD, analyze draft summary and 4–10 criteria with rationale, review, then explicit create | Text-only JD entry; analysis returns only criteria; source JD file, position summary and rationale are missing |
| G16 | P1 | Position duplicates | Canonical client/title checks plus client-scoped JD file hash checks | Exact case-sensitive title/client equality for active positions; no file hash or client canonicalization |
| G17 | P1 | Position closure | Closed/Filled/Cancelled, filled source, selected hire, original closure actor/date retained on reopening | Closed is unsupported; partial hire/reason handling but no complete closure history |
| G18 | P1 | Dashboard | Active-position candidate scope, funnel, decision mix, client portfolio, pending actions and drilldowns | Four cards plus stage bars/recent records; candidate counts use all saved history |
| G19 | P1 | Batch workspace | Per-batch CV/result mapping, per-item save state, Save Remaining for that batch | Workspace loads all assessments; save/report operations use first 50 global records rather than an explicit batch |
| G20 | P1 | CV ownership | Candidate-level master CV, retain original format, reuse across positions, deliberate refresh | Every assessment stores another blob and SaveDraft replaces the candidate's document reference |
| G21 | P1 | Stored-profile reassessment | Approved next module: assess an existing person against a new Ref using stored intelligence | Uploading a CV is required for every assessment; no reuse/refresh distinction |
| G22 | P1 | Permissions | Existing explicit create-position, talent-view and export grants; extensible policies | Admin/Recruiter role checks only; no per-action grants or assignment relationships |
| G23 | P1 | Notes and activity | Candidate/application note context and real actor/timestamp/change events | Flat text notes and generic audit entries; no history or candidate-scoped activity workspace |
| G24 | P1 | Reports | Stored evidence/confidence, appropriate client output, individual and bulk comparison | Basic HTML/PDF exists; confidence is not shown in criterion tables; batch is concatenated individual reports |
| G25 | P2 | Client master | Approved .NET module: clients, contacts and one-to-many positions | Client is a string field; no master, contacts or management page |
| G26 | P2 | Account management | User status, role/grant editing, session controls and auditable security changes | Account creation and role revalidation exist; no full account-management workflow |
| G27 | P2 | Client portal and expanded roles | Approved future product scope, scoped client/report access | Absent; must build on client relationships and permission policies |

This is not a feature-completion percentage. Some gaps are entirely absent modules; others are changed semantics inside otherwise working code.

## 4. Rules to preserve exactly

### Candidate and application ownership

`Talent Pool Master` owns the permanent person. `Candidates` owns the person + vacancy application. A rejected application does not globally reject, archive or remove that person. A person may have multiple applications and still retain one Candidate ID and one master CV.

Resolve identity in the source order: normalized email, normalized phone, then a unique normalized name. Detect multiple/conflicting contact matches explicitly. The original helper chooses the first contact match; the commercial implementation should add an auditable conflict-resolution flow rather than copying that weakness.

For the same Candidate ID + Ref #, use one application with a new assessment version. Preserve recruiter-controlled lifecycle and all previous assessment snapshots. This reconciles the source's update behavior with the specification's requirement to preserve historical AI output. A lifecycle change must never edit an assessment's AI Decision.

### Deterministic scoring

Keep score bands `0, 2, 4, 6, 8, 10` and confidence bands `2, 4, 6, 8, 10`. Match each result to the approved criterion; the model does not change weights, knockout flags, totals or decision policy.

- ATS Match = rounded weighted score percentage.
- Criterion status: score 0 with confidence at most 2 → Insufficient Evidence; score at most 2 → Fail; score 4 → Partial; otherwise Pass.
- Must-Have Fit: any knockout Fail wins; otherwise Insufficient Evidence, then Partial, then Pass. No knockouts means Pass.
- Core Role Fit = weighted average of non-knockout criterion scores; use all criteria only if every criterion is knockout.
- Evidence Strength = weighted average of confidence over all criteria.
- Fail always yields Reject. Insufficient Evidence remains Insufficient Evidence. Otherwise choose the eligible rule with the highest threshold.

The supplied workbook's rule policy is:

| Fit | Match boundary | Decision |
| --- | --- | --- |
| Pass | 75 or above | Priority Shortlist |
| Pass | 60 through 74 | Shortlist |
| Pass | Below 60 | Hold |
| Partial | 70 or above | Shortlist |
| Partial | 55 through 69 | Hold |
| Partial | Below 55 | Reject |
| Fail | Any | Reject |
| Insufficient Evidence | Any | Insufficient Evidence |

These values come from the supplied data and are configurable, not newly invented defaults. Store a rule/version snapshot with each assessment so future policy changes do not silently change history.

Nine synthetic cases were executed through the extracted original scoring functions and the current C# engine. Seven matched. Two differ:

| Case | Original functions | Current C# |
| --- | --- | --- |
| `Direct-industry experience` matched to `Direct industry experience`, with 60/40 weights and scores 8/6 | 72%, Pass, Shortlist | 24%, Insufficient Evidence, Insufficient Evidence |
| Non-knockout weighted average exactly 6.05 | Core Role Fit 6.1 | Core Role Fit 6.0 |

The C# duplicate-result guard is stronger than the original first-match behavior and should be retained with an explicit regression case. Fixing normalization does not mean accepting ambiguous or duplicate evidence.

Initial stage also needs correction: the original functions default unfamiliar/Insufficient Evidence decisions to **HR Screening**; the current C# defaults them to **Hold**.

### Lifecycle and KPI classification

Use the original controlled values, centralized with server validation:

| List | Values |
| --- | --- |
| Position status | Active, On Hold, Closed, Filled, Cancelled |
| Recruitment stage | Applied, HR Screening, Background Check, Consider, Shortlisted, Technical Interview, Final Interview, Client Interview, Offer Sent, Hired, Hold, Rejected |
| Interview status | Not Scheduled, Scheduled, Completed, No Show, Reschedule Required, Not Required |
| Client status | Not Submitted, Submitted, Under Review, Client Shortlisted, Client Rejected, Client Interview, Offer, Hired |
| Talent status | Active, Priority Talent, Passive, Placed, Archived, Do Not Contact |
| Filled source | Osynix Recruiter, Client Direct Hire, Other |

Preserve blank historical interview/client statuses as unspecified. Do not replace them during migration with invented events or operational progress.

Lifecycle synchronization from `candResolveLifecycleSync_` and specification section 13:

1. Client Hired → stage Hired.
2. Client Rejected → stage Rejected.
3. Client Offer → stage Offer Sent.
4. Client Interview → stage Client Interview; a previously completed interview resets to Not Scheduled for the new client round.
5. Submitted, Under Review and Client Shortlisted preserve the recruiter's existing stage.
6. Entering an interview stage with a blank interview status initializes Not Scheduled; it does not mean the interview has been scheduled.
7. Completed interview alone is Pending Decision, not a hire or rejection.

Candidate Database KPI precedence is **Hired/Placed → Rejected → Pending Decision → In Interview → Hold → Shortlisted**. The classifier reads client status, interview status, recruitment stage and talent status, not just the stage label. Keep person-level Candidate Database counts distinct from application-level funnel counts.

The specification leaves the **Consider** KPI bucket unresolved. Retain the stage and show it as unclassified until a business choice is made. The original candidate UI already returns no bucket for unmatched stages; do not invent a classification to make totals look complete.

The original script can reapply a persisted Client Interview status and reset Completed; the specification describes a newly requested client round. Model an explicit client-status transition/round in the target and test its repeated-save behavior. Similarly, enforce the specification's terminal Hired application rule deliberately rather than assuming the source helper provides it.

### Profile, history and API economy

Store Professional Summary and Career Profile Summary separately. Employment entries have only company, designation, start date, end date and location. Education entries have only qualification, institution, start date, end date and location. Preserve date text and precision; a stated year is not an invented full date. Render old plain text alongside new structured data without guessing missing facts.

Resolve current employment from an explicitly current record first, otherwise the latest documented end date. Validate current title/company against that record. Extract the vacancy-independent profile in the first CV assessment. Persist enough source intelligence and provenance to support the approved reassessment workflow; do not reuse another vacancy's scores or recommendation as evidence for a new role.

Opening a candidate, filtering a list, rendering evidence, changing lifecycle or generating a report must make zero AI calls. Reassessment is an explicit action; profile/CV refresh is a separate explicit action when evidence is outdated or unavailable. Missing stored evidence remains missing.

## 5. Source inconsistencies and intentionally different choices

- `candidatedatbasejavascript.txt:3415` calls `saveCandidateProfileOperational`, but no function with that name exists anywhere in the supplied text/GS folder. `updateCandidateLifecycle` does exist. The supplied profile-save path therefore has an unresolved dependency; build the target operation from the documented contract rather than treating the button as proof of a complete backend.
- Candidate Profile Notes/Activity have UI structure, but the Word document identifies those modules as pending. The supplied profile service does not return an activity feed. Existing Activity Log events are real; they do not prove candidate-level events already exist.
- `requestAccess()` only displays text; it does not submit an administrator approval request. Navigation's Profile, Settings and Help entries are labelled Soon. Treat these as future work, not completed source features.
- The older `buildCandidateDatabaseSummary_` aggregates mainly by stage. The newer frontend `getCandidateLifecycleState_` applies client/interview/talent precedence and matches the Word specification. Use the latter semantics for the target domain service.
- Dashboard pending-action code tests a client status `pending`, which is absent from the workbook's current client list. Preserve the intended actionable queue using the supported Submitted/Under Review/Client Shortlisted/Offer conditions, with explicit tests, rather than adding a new hidden status.
- Assigned clients/references are stored and returned by authentication, but the supplied dashboard explicitly allows Admin and Recruiter to see all active positions. Do not retroactively claim the source already scopes all queries by assignment. Define scopes in policies for the approved target and retain the documented operational visibility until changed deliberately.
- The original login uses email OTP and trusted devices. This workspace now has the user-requested password-based `Admin` account and username sign-in. Preserve that authorized login. OTP equivalence and remembered-device administration are additional authentication decisions; they are not a reason to replace the working account.
- The document calls for normalized structured history; the existing .NET migration notes describe plain-text history as a limitation. Treat the specification as the rebuild requirement.
- Do not introduce WhatsApp, ChatGPT/MCP or connector billing into this migration; the specification explicitly excludes them.

## 6. Target design

Keep .NET 10, Blazor Interactive Server, EF Core and SQLite. Preserve useful existing code for Identity, short-lived contexts, optimistic concurrency, transactional saves, strict AI result handling, deterministic scoring and escaped HTML reports. Expand the model around the business ownership rules.

### Proposed entities and responsibilities

| Entity/aggregate | Responsibility and important relationships |
| --- | --- |
| Client and ClientContact | Canonical client identity, contacts and recruitment context; one client owns many positions |
| Position | Ref, client, title/location, priority, target, open date, contact, assignments, JD document, status and closure history |
| CriterionSet and Criterion | Approved/versioned framework with weights, knockout, expected evidence, guidance, notes and original draft rationale |
| Candidate | Permanent GUID plus unique display/legacy Candidate ID, contacts, location, summaries and talent status |
| CandidateEmployment and CandidateEducation | Ordered structured rows, source date text/precision and provenance; legacy text retained separately |
| CandidateDocument | Candidate-owned master CV/document metadata, content hash, current version and refresh history |
| Application | Unique candidate + position relationship and recruiter lifecycle, interview/client status, salary and availability |
| AssessmentVersion and CriterionResult | Immutable results under an application, profile/criteria/policy versions, model, source evidence and score/confidence/rationale |
| AssessmentDraft, AssessmentBatch and BatchItem | Server-owned review state, per-file/result mapping, per-item failures and explicit saved state |
| CandidateIntelligence | Vacancy-independent extracted facts/source references for intentional reassessment; no reuse of unrelated role scores |
| CandidateNote and ApplicationNote | Correct note context, author, timestamps and edit history |
| ActivityEvent/AuditEvent | Real actor, entity, previous/new values, time and application/client/candidate context |
| UserProfile, PermissionGrant and assignments | User status, composable policies and authorized client/position relationships |
| ReferenceList and DecisionPolicy | Validated workflow choices and versioned thresholds |
| ImportBatch and ImportRecord | Workbook/source hash, original row, typed mapping result, warnings and reconciliation |

Use foreign keys for real relationships, including candidate document ownership and application/position/hire links. Maintain uniqueness by stable identifiers; normalized names are lookup aids, not permanent-person keys. Imported date-only values retain their original date semantics; new events use UTC with Asia/Karachi display where appropriate.

The file-storage boundary should carry metadata and authorization independently of its provider. An initial SQLite/local provider can preserve existing blobs; a later object-storage provider should not change candidate business logic. File deduplication and refresh history must be deliberate. Moving storage must preserve downloads and existing document references.

### Application services

Split the growing `AtsService` into focused services: client/position management, candidate identity and profile, assessment orchestration and scoring, application lifecycle, notes/activity, dashboard queries, report generation, permissions, storage and migration. Keep components responsible for presentation and explicit user actions. Do not expose EF entities as unrestricted edit/save contracts; use operation-specific input models.

Use common services for interactive components, report/file endpoints and future client access. Enforce permissions and record scope on server operations, not only navigation visibility. Limit a recruiter's batch actions to an explicit batch and authorized items. Audit report exports, security changes and lifecycle field changes as well as saves.

### Recruiter workspaces

- **Dashboard:** active-position operations, pending queues, client portfolio, decision mix, funnel and drills into the same filtered views.
- **Manage Positions:** search/client/status/priority filters, sort/page, position metrics, create/edit/JD review, closure/reopen and applications. Create Position stays inside this module.
- **Candidate Assessment:** client → active position, visible approved criteria, single/batch upload and server-owned drafts. Save Remaining and batch reports act on the selected batch.
- **Candidate Database:** permanent-person list with latest/selected application context, lifecycle queues and a large profile drawer.
- **Candidate Profile:** Overview, Assessment, Notes and Activity; candidate ID/contact/current location/CV actions in the header; application selector; structured timelines; closing preserves the underlying filter/page/scroll state.
- **Talent Pool:** vacancy-independent sourcing, taxonomy filters and explicit reassessment against a selected position.
- **Clients:** client/contact management and position relationships.
- **Administration:** users, status, grants, assignments, lists, decision policy, audit and session administration as implemented.

The pipeline can remain a useful alternate application view, but lifecycle editing must use the same service and profile controls. It must not become a second independent lifecycle implementation.

## 7. Implementation sequence and completion gates

### Phase 1 — Preserve data and establish the schema upgrade path

Add source-based regression cases for the two scoring differences, Candidate ID relationships, original list values and lifecycle precedence. Design the expanded schema and a tested baseline/upgrade path for the existing EnsureCreated database. Back up and test upgrades on copies, preserving Identity user IDs/password hashes and existing business data. Replace initialization only after that upgrade path is proven.

Repair typed migration mapping for criterion JSON, source timestamps, Candidate ID relationships, both summaries, structured/legacy history, positions and historical events. Add a reconciliation report that compares source and target values rather than only counts. Do not reactivate old Google sessions or treat missing historical scores as zero.

**Gate:** the supplied workbook rehearses as 5 positions, 41 criteria, 42 people and 43 applications; both evidence-bearing applications retain their actual JSON; the other 41 remain explicitly without detailed evidence; historical dates and CAND-031's two references survive; existing Admin login still works after an upgrade rehearsal.

### Phase 2 — Restore core domain behavior

Implement original lists, lifecycle synchronization and KPI precedence; original criterion normalization and rounding; identity matching and explicit ambiguity handling; human Candidate ID generation; one application per person/reference with assessment versions; candidate-owned CV reuse and current-employment fallback. Keep lifecycle edits independent of AI decisions.

**Gate:** source-based scoring cases match, the same person matches by phone when email is absent, same-reference reassessment preserves recruiter progress and history, different-reference assessment preserves identity/CV, and Hired/Rejected/Pending/Interview precedence is consistent across screens.

### Phase 3 — Complete Clients and Manage Positions

Introduce the client/contact master and map existing client strings to explicit records with reviewed duplicate resolution. Add priority, hiring target, contacts, assignments, original JD upload/hash, draft position summary and criterion rationale. Complete filters, metrics, closure source/hire linkage and retained closure history on reopening.

**Gate:** JD analysis persists no live position; explicit create saves the reviewed approved framework; duplicate checks and weights are validated; Closed/Filled/Cancelled and reopening retain accurate history; all position metrics have reconciled application inputs.

### Phase 4 — Rebuild Candidate Database, Profile and Talent Pool

Implement the locked drawer/tabs, application selection and lifecycle controls; structured career/education with legacy fallback; separate summaries; header CV actions; Notes/Activity and permanent-person filters/queues. Create a separate Talent Pool sourcing/reassessment entry point. Share profile components and query contracts.

**Gate:** structured and legacy workbook profiles render correctly; empty historical fields remain absent; original application evidence reopens without AI calls; closing the drawer restores the list context; notes/activity show actual stored events and appropriate context.

### Phase 5 — Complete assessment, reassessment, batch and reports

Introduce AI/storage interfaces, source-intelligence provenance and explicit refresh/reassessment modes. Implement server-owned batch items and retry/save/report behavior; preserve original file/result association and per-item failures. Complete criterion confidence, client report content and a real comparative summary report, with approved branding and long-document page checks.

**Gate:** viewing data/reporting makes no provider calls; stored-profile reassessment uses no CV re-read unless explicitly requested or evidence is unavailable; Save Remaining skips saved items and other batches/users; reports contain stored scores/evidence, exclude contact/internal-note fields as required and pass PDF layout review.

### Phase 6 — Dashboard and administration, then approved expansion

Build active-position KPI queries, pending queues, client portfolio and consistent drilldowns. Introduce composable permissions and enforce them across services/endpoints; map current grants and user status; add user/grant/session administration. Build scoped client portal and broader roles only after client relationships, authorization and reporting are proven.

**Gate:** inactive-vacancy applications do not inflate active operational dashboard counts; every card's filter matches its displayed count; denied actions are rejected server-side; audit contains real field-change/security/export events; a client user can access only approved client records/reports.

## 8. Acceptance scenarios

| Scenario | Required result |
| --- | --- |
| First CV assessment | One intentional AI call supplies both reusable profile and vacancy evidence; review precedes Save to ATS |
| Existing email with changed display name | Reuse the existing Candidate ID; do not create another person |
| Existing phone without email | Reuse the existing person when the contact match is unambiguous |
| Two different people sharing a name | Keep separate identities; require explicit resolution where contacts do not distinguish them |
| Same person, same Ref reassessment | New assessment version on one application; preserve lifecycle, previous AI output and master CV |
| Same person, new Ref | New application under the same Candidate ID; original application is unchanged |
| Knockout fail at high match | Reject regardless of other criterion scores |
| Missing knockout evidence | Insufficient Evidence, not a fabricated pass/fail |
| Criterion punctuation variation | Same match semantics as the source normalizer; duplicate ambiguity still earns no credit |
| Core fit exactly 6.05 | Source-compatible 6.1 display/result |
| Client Rejected with old interview Completed | Rejected queue; lifecycle updates stage; AI Decision remains unchanged |
| Client Offer | Offer Sent stage and Pending Decision classification |
| New Client Interview after Completed | New interview round initialized Not Scheduled |
| Interview Completed alone | Pending Decision, not Hired |
| Position close and reopen | Active dashboard scope changes; previous close actor/date remain available |
| Workbook evidence populated | Import and display the actual criterion JSON |
| Workbook evidence missing | Show real framework with unavailable scores/evidence; no reconstructed zeros |
| Structured and legacy career data | Separate structured entries or faithful legacy text; no guessed dates |
| Profile open/close | No AI call; selected application and list context retained |
| Batch retry/Save Remaining | File/result mapping remains correct; completed items are not repeated or mixed with other batches |
| File/report request without permission | Server denies access according to the same policy as the UI |
| Database upgrade | Existing users, identifiers, snapshots and files survive a tested upgrade and restore rehearsal |

## 9. Verification performed

- `dotnet test Osynix.Ats.slnx --no-restore --verbosity quiet`: **11 passed, 0 failed, 0 skipped**. These existing tests verify selected scoring/storage/import behavior, not complete source parity.
- Actual-workbook preview/commit against in-memory SQLite: **5 positions, 42 candidates, 43 assessments**; six Pass/Partial rule thresholds correct; **0 imported criterion-evidence records despite 2 populated source records**.
- Original scoring functions executed locally against nine synthetic cases and compared with the C# engine: **7 match, 2 differ**, recorded in the audit JSON.
- Workbook extraction compared formula expressions and cached values, controlled lists, record keys, date types, structured-field shapes and real activity counts. The workbook was not modified or recalculated.
- Static comparison covered application services, models, persistence, Razor routes/components, report/file routes, login, existing tests, original backend function inventory, frontend server calls, DOM tab/action structure and relevant styling.

No paid provider requests, live Sheet writes, user emails, new login accounts or production migration were performed as part of this review. The temporary databases and synthetic cases do not establish live Google, email, AI or PDF-layout compatibility.

## 10. Source coverage map

| Source files | Workflows/evidence reviewed |
| --- | --- |
| Config.gs, Code.gs, Appsscriotjason.txt | Sheet ownership, timezone, route/bootstrap/details and deployment configuration |
| Authservice.gs, AuthroizationServices.gs, AccessGuard.txt | OTP, trusted device/session behavior, user status, role/permission helpers and route checks |
| Loginhtml.txt, loginjavascript.txt, loginstylehtml.txt | Original login layout/actions, verification and request-access behavior |
| Positionservice.gs, SheetServies.gs | JD draft schema, criteria/weights, client canonicalization, reference allocation, duplicate checks and file handling |
| Positionhtml.txt, Positionjavascripthtml.txt, Postionstyle.txt | Client selection/create, JD upload, reviewed summary/criterion editor/rationale and explicit live create |
| ManagePositionService.txt, Managepositions.txt, Managemeposotionjavascript.txt, Nanagepositonstyle.txt | Listing counts, client/status/priority filters, sort/page, position edit and state changes |
| candidateservice.gs | Identity matching, same-reference upsert, lifecycle preservation/synchronization, permanent profile and master CV behavior |
| OpenAI.txt | CV/profile/evidence schema, structured history, latest employment fallback, normalization, deterministic metrics and decision rules |
| Indexhtml.txt, Javascripthtml.txt, Stylehtml.txt | Client/position context, single/batch state, per-item save/View, result tabs, report actions and messages |
| CandidateDatabaseServies.txt, CandidateProfileservices.txt | Master/application joining, latest/selected reference, criterion JSON/fallback and controlled-list options |
| Candidatedatabasehtml.txt, candidatedatbasejavascript.txt, Candidatedatabasestyle.txt | Candidate filters/KPIs, lifecycle precedence, locked drawer/tabs, timelines, notes/activity rendering and missing save dependency |
| Talenetpool.txt, TalentPooljavascript.txt, Talentpoolstyles.txt | Separate talent repository, taxonomy filters/pagination and profile navigation |
| DashbaordServices.txt, Dashbaord.txt, Dashbaordjavascript.txt, Dashboardstyle.txt | Active-position scope, KPIs/funnel/mix/client portfolio, pending actions, drilldowns and closure audit |
| Navigation.txt | Original navigation, permission visibility and placeholders; approved consolidation compared with specification |
| ReportServices.txt | Single/batch report inputs, comparison/individual sections, evidence, branding, hidden contacts and pagination intent |
| Osynix_ATS_Master_Tracker.xlsx | All 13 sheets, headers, populated keys, formula families/cached values, lists, JSON and historical metadata |
| Osynix_ATS_Functional_Business_Logic_Migration_Specification.docx | All 26 sections and 13 tables, separating implemented reference, approved rules and pending/commercial scope |

The next implementation should start with Phase 1 and Phase 2. Those phases establish the data and behavior contracts needed by the remaining workspaces and approved modules.
