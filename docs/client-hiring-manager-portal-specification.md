# Client / Hiring Manager Portal — Recommended Functionality

## Implemented approval — 26 October 2026

The first approved **Client Portal — Overview** design is used for clients and hiring managers. Existing `/review/access` invitations open this portal through the existing protected session; the link journey mockup is not used as a second interface. Portal invitations do not create ATS identities, roles or team memberships.

Starter and higher plans include portal access for Freelancer, Employer and Agency subscriptions. In **Clients → Create / Open Client → Contacts**, each contact has a portal access checkbox. In Employer **Positions → Vacancy Details**, hiring manager details can be recorded on every plan; the portal checkbox is available on eligible paid plans. Save provisions the access grant and queues an invitation if an email is provided. Contact IDs remain stable when editing, and disabling access preserves recruitment history.

Client contacts see work deliberately shared for their client. New position hiring manager grants are limited to that position. Existing client links and protected tokens are preserved for testing; converted Employer links also respect position portal checkboxes. Changing a recipient's email replaces their scoped token and retires affected older shared links.

Submitting profiles without eligible portal access records the review activity and queues only a pending-profile count to the provided hiring manager email. It includes no candidate profile, assessment, report, or access link. Portal messages appear in the assigned ATS team's existing Messages inbox, with a client-facing reply option and dated history.

Portal email invitations wait for configured email delivery and a public HTTPS application address. Notification-only emails contain no portal links and need configured email delivery. Failed deliveries are retried. Invitation tokens remain encrypted at rest and are inserted into outgoing email only at delivery time.

The migration converts existing access flags without replacing tokens, assessments, submissions, or contact identities. The app creates its normal pre-migration database backup before applying schema changes.

## Product boundary

The Client / Hiring Manager Portal is a limited, operational workspace for reviewing recruiting work shared by an Employer or Agency. A portal identity is **not** an Employer, Agency, Freelancer, recruiter, or ATS workspace account. Signing in to the portal never grants access to subscription settings, billing, workspaces, user administration, talent pools, candidate sourcing, other clients, or internal ATS functions.

The existing Hiring Manager review link already supports client-scoped candidate submissions, feedback, and interview availability. The portal should turn that focused flow into a durable, easy-to-return-to workspace without broadening its data scope.

## People and access

| Person | Access | Cannot do |
|---|---|---|
| Client / Hiring Manager | View only positions and candidates explicitly shared with their client/contact; review, comment, and respond to recruiting tasks | Open the ATS workspace, change subscription or position setup, see internal recruiter notes, view unshared applicants, contact candidates through ATS, or manage another client |
| Employer / Agency workspace owner | Invite or remove client reviewers; choose which positions and profiles are shared; manage the review link and existing ATS permissions | Treat a client reviewer as an internal team seat unless explicitly invited as a separate ATS team user |
| Recruiter / assigned ATS team | Share profiles, coordinate interview steps, reply to client messages, and update the ATS workflow | Expose internal notes, private assessment evidence, or unrelated candidate history in the client view |

Access should be bound to a named client contact and scoped to that client. Position and candidate visibility is limited to records deliberately shared with the client. Prefer individual invitations and auditable sign-in over a single link forwarded to an unbounded audience. Existing link access can remain as a controlled fallback during transition; owners must be able to replace or revoke it.

## Recommended navigation

1. **Overview** — short status summary, urgent requests, upcoming interviews, and recent activity.
2. **Positions** — assigned openings with hiring progress, recruiter, last update, and open actions.
3. **Candidates** — profiles submitted for this client, filterable by position and review state.
4. **Interviews** — requested availability, confirmed interviews, reschedule proposals, and completion review.
5. **Messages** — conversation with the assigned recruiter / ATS team, grouped by position and candidate where relevant; general client support is a separate support thread.
6. **Company & access** — the signed-in reviewer’s contact details, invited reviewers, and access help. Only the workspace owner manages the client’s reviewer list.

Navigation should hide sections with no accessible data, but must not display ATS-wide menus such as Manage Positions, My Talent Pool, Master Talent Pool, My Team, Integrations, or Subscriptions.

## Main workflows

### 1. Review a shared candidate

1. Open a position from Overview or Positions.
2. Open a submitted candidate in Candidates.
3. Read the client-safe profile, position context, recruiter note, and review history for this client/position.
4. Choose a next step: **Shortlist**, **Request interview**, **Need more information**, **Hold**, **Reject**, **Recommend hire**, or **Request offer**.
5. Add a client-facing note. A request for interview may include preferred format and one or more availability windows.
6. The portal confirms the response and records who submitted it and when. The ATS owner receives an actionable notification and can coordinate next steps.

The client expresses a recommendation; the ATS workspace owner retains control of recruitment status, interview invitations, candidate email, and offer execution. “Request offer” is not an offer sent to a candidate.

### 2. Coordinate an interview

- The client can propose availability and indicate online or on-site preference, location, time zone, and duration where the workspace plan supports the relevant workflow.
- The ATS team confirms the schedule, checks the subscription’s interview/email entitlements, and sends any candidate-facing invitation from the ATS.
- The client sees a clear state: **Availability requested**, **Awaiting recruiter confirmation**, **Confirmed**, **New time proposed**, **Cancelled**, or **Completed — feedback due**.
- The client can confirm completion and submit interview feedback. They cannot send the invitation directly to the candidate through the portal.

### 3. Respond to a recruiter question

- A recruiter can request specific information on a candidate or position.
- The request appears as a task in Overview and as an unread item in Messages.
- The client responds in the task or its linked conversation; optional supporting files are attached to that response.
- The task changes to **Response submitted** for the recruiter. The recruiter reviews it and closes the task or asks a follow-up.

### 4. Message and track history

- Keep separate, searchable threads for each client/position or client/candidate discussion, plus a separate general support conversation.
- Show the subject, position/candidate context, unread marker, last activity, and open/closed state in the inbox.
- Store a chronological, auditable history in the ATS. A client reviewer sees their own client-scoped history; internal-only notes remain private.
- Allow an authorized client reviewer to close a conversation and download their visible conversation history. Closing a thread does not erase the ATS record.

## What the client can see

Show the candidate name, professional headline, current role/employer when present, location, experience, skills, client-safe profile summary, position applied/shared for, recruiting stage, client response state, and a clear assessment summary only when the workspace intentionally shared it. Include the submitted date and the recruiter’s client-facing note.

Do not expose raw CV files, phone/email, source details, private assessment versions or evidence, internal ATS scores/thresholds, recruiter-only notes, other employers’ applications, global talent-pool history, or another client’s feedback by default. If a client-safe CV sharing option is introduced later, it must be an explicit per-share choice and be logged.

## Notifications

- In-portal notification for newly shared candidates, interview actions, recruiter questions, messages, and status changes.
- Email notification to the reviewer’s verified address when a new actionable item arrives, using the existing account email integration when enabled. Email delivery must not be claimed until configured and confirmed.
- Notifications link directly to the relevant portal record after access verification. Do not include sensitive candidate details or attachments in notification previews.
- Provide per-reviewer read/unread state and an optional digest setting.

## Subscription and permission rules

- The existing **Hiring Manager review portal & links** entitlement controls whether an Employer/Agency may create or maintain external client access, as defined by the approved subscription catalogue.
- A plan change may enable or disable the workspace’s ability to share new/reviewable work according to the existing entitlement behavior. It never converts a portal reviewer into an ATS account or grants other modules.
- The portal has no independent subscription, no payment screen, and no ability to purchase or upgrade a workspace plan.
- User account type and role are not inferred from the portal link. Portal authorization is a separate, least-privilege client/contact grant.

## Owner controls and audit

The Employer/Agency ATS workspace should be able to view invited contacts, last access, positions shared, invitation status, and link state; resend an invitation; revoke one reviewer; replace a client link; and inspect a dated activity log. Revocation blocks future access immediately but preserves the ATS record and audit history.

Record candidate sharing, link/invitation creation and revocation, profile views, feedback submissions, interview proposals, message sends, attachment metadata, exports, and status transitions with actor, client, position, candidate, and timestamp. Apply a retention policy centrally; client users cannot delete the retained ATS audit record.

## Suggested release sequence

**Phase 1 — Client operations portal:** authenticated client/contact access; Overview, Positions, Candidates, Interviews, and Messages; existing feedback choices; interview availability; recruiter notifications; client-scoped audit and revocation.

**Phase 2 — Team review:** multiple named client contacts, position-level reviewer assignments, reviewer roles (reviewer vs. client access owner), message attachments, digest controls, and exportable conversation history.

**Phase 3 — Integrations:** email delivery and calendar/provider scheduling using the workspace owner’s enabled integrations and subscription entitlements. The portal remains operational-only and does not become a separate ATS workspace.

## Acceptance checks

- A client reviewer cannot navigate to or query an unshared client, position, candidate, talent pool, or ATS module.
- A reviewer can complete the supported feedback and availability workflows without an Employer/Agency/Freelancer account.
- Every submission is attributed to the signed-in reviewer and appears as an actionable update in the ATS.
- Messages are grouped into separate, dated client-scoped sessions; unread items and open tasks are visible from the portal Overview.
- Revoking a reviewer or link prevents further access while preserving review and audit history.
- Candidate contact information, CV bytes, and internal assessment history remain hidden unless a later, explicit client-safe sharing capability is approved.
