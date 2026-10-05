# Migration status and parity boundaries

The source-based rebuild is integrated and builds in `Osynix.Ats.slnx`. [Rebuild progress](rebuild-progress.md) lists completed workflows and verification. [The original analysis](rebuild-analysis.md) and source-audit JSON retain the initial baseline findings.

The implementation now includes client/position management, stable candidate identities, immutable assessment versions, source lifecycle synchronization, the four-tab profile workspace, structured timelines, contextual notes/activity, stored-profile reassessment, owner-scoped batches/retry, comparison/version-aware reports, active-position dashboard drilldowns and account/grant administration.

Persistence uses committed EF migrations, backups before upgrades, validated starter-schema baselining, source timestamps and short-lived contexts. The working database was upgraded with unchanged existing credentials and business counts. The actual workbook was imported and reconciled only in a separate rehearsal database.

## Current limits

- Admin/Recruiter roles and three source grants are enforced. Client/Manager roles and scoped client portal are not yet implemented; imported assignment strings remain informational metadata.
- No live Sheet synchronization, automatic Drive-file fetch, OTP/email invitations, MFA, session/trusted-device management, outbound notifications, automatic talent recommendations or merge/split UI.
- Lists currently paginate in memory; large-data indexing/filtering and performance testing remain separate work.
- UI is a Blazor workspace informed by the reference source, not a claim of pixel-identical parity with a deployed Apps Script screenshot.
- Historical evidence exists only where the workbook actually stored it. Missing scores/evidence remain unavailable; no import reconstructs historical AI output.
- Provider behavior is covered with a mock HTTP fixture. Live model/schema/document acceptance and original-logo approval are still needed. PDF rendering requires a server browser; installed Windows Edge can be used as a fallback.
- SQL Server/PostgreSQL, external object storage, WhatsApp and MCP integration are future extensions.

## Cutover gates

1. Use a fresh source export and compare deployed Apps Script behavior with the audited source snapshots.
2. Import into a separate database, reconcile source IDs/counts/dates/evidence/statuses, review decision rules and lock criteria.
3. Run representative live JD/CV cases with the configured provider and verify profile facts, scores, knockout evidence and same-reference version behavior.
4. Approve original branding and single/comparison PDFs, including long-table continuation and contact exclusions.
5. Restore-test database backups and preserve ASP.NET data-protection keys; choose an old-app write-freeze/cutover window.
6. Keep the old application and source files available for rollback until operational acceptance.

No live Google Sheet writes, paid AI requests, emails or deployment were performed by this build.
