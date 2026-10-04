# Workbook mapping

First row is treated as headers. Required header lookup is case-insensitive. Blank rows are skipped. The whole import is atomic and allowed only when no positions, candidates or assessments exist. Administrator accounts may already exist.

| Source sheet | Source columns | Target |
|---|---|---|
| Positions Master | Ref #, Position/Position Title, Client, Location, Salary Range/Salary / Budget, Employment Type, Status, Job Description/JD Text, Client Notes/Notes | Positions |
| JD Criteria | Ref #, Criterion, Weight %, Knockout?, Evidence Expected, Scoring Guidance, Notes | Criteria related to position |
| Talent Pool Master | Candidate ID, Candidate Name, Mobile / Phone Number, Email ID, Location, Current / Latest Designation, Current / Latest Company, Total Experience (Yrs) | Candidate identity and current profile |
| Talent Pool Master | Primary Function, Secondary Function / Specialization, Industries / Domains, Core Skills, Seniority Level, Systems / Tools, Geographic / Market Exposure | Vacancy-independent talent attributes |
| Talent Pool Master | Professional Summary/Career Profile Summary, Employment / Career History, Education, Talent Pool Status, Talent Pool Notes, Source CV / File | Profile narrative, status and original document link |
| Candidates / Recruitment Pipeline | Candidate Name, Ref #, ATS Match %, Must-Have Fit, Core Role Fit /10, Evidence Strength /10, Decision/ATS Decision, Key Strengths, Key Gap / Risk, Executive Assessment | Historical assessment + immutable imported snapshot |
| Candidates / Recruitment Pipeline | Recruitment Stage, Interview Status, Client Status, Expected Salary, Availability, Recruiter Notes | Current recruitment lifecycle |
| Lists & Settings | M: Must-have fit; N: minimum ATS percentage; O: decision; rows 2–100 | Configurable decision rules |
| Every sheet | Every displayed data-cell value, with row number and workbook hash | ImportRows raw JSON archive |

Date fields not explicitly mapped to operational entities remain in raw imported rows. Operational CreatedUtc records the import time, not the original screening time. CV links are retained as text, with HTTPS links available in candidate details; hyperlinks stored only as cell metadata may need a mapping extension. Original workbook formulas/macros are never executed by the application. The archive contains displayed values, not the original binary workbook; keep the workbook backup separately.

Duplicate normalized candidate names are rejected during initial import for manual resolution, rather than silently merging different people. Candidate reference IDs are normalized to three digits when numeric; nonnumeric references are retained. Unsupported or missing criteria stay unlocked and require review. Existing historical assessments do not unlock or rewrite new AI assessments.
