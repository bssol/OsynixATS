# Workbook mapping

Headers are read case-insensitively from the first row. Blank rows are skipped. Initial import is atomic and allowed only when no positions, candidates or assessments exist. Existing login accounts and their credentials are preserved.

| Source | Operational target |
| --- | --- |
| Positions Master | Reference/title/client, location/salary/type/status, priority/target/contact/recruiter, JD link/hash/text, created/open/update/closure metadata, notes, filled source and hired Candidate ID |
| JD Criteria | Position relationship, criterion, weight, knockout, evidence expectation, scoring guidance and notes; imported frameworks stay unlocked for review |
| Talent Pool Master | Original Candidate ID/contact/location/current employment/experience, reusable taxonomy attributes, separate professional and career summaries, talent status/notes, CV link and screening dates |
| Employment / Career History and Education | Typed records when actual JSON arrays are present; date precision/source text retained; legacy narratives remain visible without guessed records |
| Candidates | Candidate ID + Ref # relationship; source score/decision/core/confidence, executive summary/strengths/gaps and original Criterion Results JSON; immutable initial assessment version |
| Candidates lifecycle | Source stage/interview/client status, salary, availability and recruiter notes; blank statuses remain unspecified |
| Lists & Settings M:O | Original Pass/Partial decision thresholds; future new assessments use reviewed stored rules |
| Lists & Settings controlled columns | Reference options retained with list name/value/order; operational controls use the audited source vocabulary |
| Activity Log | Real timestamp/actor/action/entity/details and before/after values; candidate links when the entity ID identifies a person |
| Users & Access | Missing Admin/Recruiter accounts, source name/status/grants and assignment metadata; imported accounts have no password until Admin sets one |
| Source data sheets | Displayed values retained as row JSON with sheet/row/workbook hash for reconciliation, except Google session/trusted-device token sheets |

Source screening/open/closed date fields retain the original date/time precision. Source timestamps used for UTC record/audit fields are converted from the Apps Script `Asia/Karachi` timezone. The importer reads cached date values rather than treating a missing date as the import date. It does not recompute historical scoring.

Missing historical aggregate or criterion metrics remain null/unavailable. Only the two actually populated criterion JSON records in the supplied workbook have detailed historical evidence; the other 41 applications cannot acquire evidence through import. Assessment/profile views can display the position framework with unavailable results. Reports clearly disclose when no original criterion evidence exists.

Candidate ID is authoritative on workbook rows. The same ID can have different position applications. Different IDs with the same display name remain separate people. A repeated Candidate ID + Ref # pair fails the import for review, rather than silently overwriting it. Without source IDs, email/phone/unique-name matching is used and contact conflicts are rejected.

Numeric references are normalized to three digits. `Recruitment Pipeline` is an aggregate archive, not a substitute for the `Candidates` application sheet. Google session tokens and trusted devices are not imported. Existing account passwords/status/roles are not overwritten by workbook accounts. Assignment metadata is retained for future scoped authorization; it does not restrict the current company-wide Admin/Recruiter role model.

CV/JD Drive links remain HTTPS links. The application does not fetch linked file bytes. New CV/JD uploads are stored behind `IDocumentStore` with content hashes. Keep the original XLSX backup separately: the raw JSON archive does not preserve original formatting, charts, formulas or hyperlink-only metadata. Import is not ongoing Sheet synchronization.

Rehearsal result for the supplied workbook: 5 positions, 41 criteria, 42 people, 43 applications/initial versions, 2 detailed evidence records and 57 real activity events. Neither source workbook contents nor live Sheet data were changed.
