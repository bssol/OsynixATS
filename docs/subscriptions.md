# Subscription plans and manual activation

Users open **Settings → Subscription** for their current plan, live workspace usage, plan comparison and upgrade-request history. Freelancer, Employer and Agency workspaces have separate catalogues. Owner-only settings do not expose another workspace's subscriptions.

Admin opens **Workspaces & Subscriptions → Manage subscription plans & pricing** to edit the plan name and description, PKR/USD monthly and annual prices, promotion discount/label/dates, draft/publication status, capacities and included features. The **Plan benefits, limits & Careers page** section controls position slots, internal seats, monthly AI assessments, monthly JD analyses, Careers page application caps and other entitlements. The **Preview plan** button shows the current draft as a customer-facing card before it is saved. Unconfirmed prices display **Price on request**. Saving a plan notifies connected subscription screens immediately. Pending requests and active subscriptions retain their saved prices and benefits; changing a public price does not rewrite earlier agreements.

## Activation

1. The owner selects a published plan, billing period and currency, reviews its benefits and submits an optional note.
2. The request appears under that workspace's Requests panel in Admin. Existing access remains active.
3. Admin verifies payment outside ATS, checks the payment-confirmation box, enters a response and grants the request.
4. Server-side authorization requires both request-review and subscription-management permissions. Approval is rejected without payment confirmation. Saved terms are applied once. A newly activated plan starts a fresh AI allowance; historical usage is retained in its original allocation.
5. The user sees the response in upgrade history and notifications. Included access takes effect immediately. Admin can revert the workspace to Free; separate explicit Admin grants remain visible and are retained.

Paid plan activations receive a renewal date based on the agreed Monthly or Annual period. Admin can amend it in that workspace's **Subscription & Capacity → Renewal date** control. Seven days before that date, the system sends the owner an email and adds a persistent notification in the portal. Dates are interpreted in Pakistan time. The reminder asks the owner to contact Admin; it does not charge, renew, suspend or change access.

There is no payment gateway, card collection, automatic charge, recurring debit or automatic overage billing. Billing period records the agreed subscription choice. Upgrades activate after offline payment confirmation; owner-requested cancellations and downgrades are applied automatically on their scheduled renewal date.

Promotion discounts apply to the selected billing period's displayed base price. Optional start and end dates use Pakistan time. During an active promotion, users see the reduced price alongside the crossed-out standard price and the campaign label. Upgrade requests store the final quote and the exact plan snapshot so later catalogue edits do not change an existing request.

## Cancellation, downgrade and upgrade timing

Owners use **My subscription → Cancel subscription** to schedule cancellation, or choose a lower tier under **Compare plans**. The confirmation shows the effective renewal date in Pakistan time. The current plan, limits, and included Marketplace visibility stay active until that date. Cancellation moves the workspace to Free; a downgrade applies the saved lower plan at renewal. Existing data, jobs, memberships and conversations are retained. New activity is constrained by the lower plan's capacity.

The upcoming change is shown on the subscription page and in the admin account's active subscription panel. **Keep current plan** withdraws it before it takes effect. A replacement cancellation/downgrade keeps a history entry for the replaced schedule. Changing the renewal date in Admin moves the pending change to the same date; removing the date is blocked while a change is scheduled.

Upgrades are requested through the existing offline payment flow and activate immediately when Admin confirms payment and grants the request. They do not wait for renewal. Each activation receives a new AI allocation: old unused credits are discarded and old usage records remain available for audit. An approved upgrade supersedes any scheduled cancellation or downgrade. Scheduling cancellation/downgrade withdraws outstanding upgrade requests; withdrawn requests cannot subsequently be granted.

Scheduled changes preserve the target plan's benefits and quoted price even if Admin later edits or unpublishes the catalogue entry. Paid downgrade billing remains offline; no automatic debit is made. The next renewal date is anchored to the scheduled effective date and selected Monthly/Annual period.

A durable processor applies due changes on startup and every minute, including overdue changes after downtime. Subscription views, quota checks and Marketplace checks also enforce due transitions when accessed. Each schedule is applied once in a transaction. Renewal payment reminders are suppressed while a cancellation/downgrade is pending to avoid prompting payment for the plan that is ending. The separately labelled Admin **Revoke paid access now** control remains an immediate administrative override.

Existing subscriptions and recorded consumption are preserved by the allocation migration. It does not schedule or cancel any live subscription.

## Default Freelancer catalogue

| Plan | Position slots | Assessments/month | JD analyses/month | Careers applications/position | Marketplace | Master pool | Integrate Meet/Zoom or MS Teams |
| --- | ---: | ---: | ---: | ---: | --- | --- | --- |
| Free | 1 | 20 | 2 | 10 | No | No | No |
| Starter | 3 | 50 | 5 | 10 | Yes | No | No |
| Growth | 10 | 200 | 15 | 15 | Yes | Yes | Yes |
| Pro | 25 | 500 | 30 | No per-position cap; monthly AI limit applies | Yes | Yes | Yes |

Freelancer plans never include internal team seats. All have core recruitment, clients, the private talent pool and a Careers page (with the per-position application cap shown on the plan). Report exports start with Starter. Email Templates remain an explicit Admin grant until the catalogue's feature is enabled. Master pool access included in a paid plan does not require a second grant. Plan comparison cards mark new or increased limits—such as position slots, assessments, JD analyses and Careers page application caps—against the preceding tier. Meeting access is labelled **Integrate Meet/Zoom or MS Teams**; provider OAuth connections remain a separate integrations phase.

## Approved Employer catalogue

Employer accounts use the same **My subscription / Compare plans / Upgrade requests** format as Freelancers. The Employer owner does not consume an internal seat. Active and on-hold positions share the workspace allowance.

| Plan | Slots | Internal seats | Assessments/month | JD analyses/month | Monthly PKR | Annual PKR | Monthly USD | Annual USD |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Free | 3 | 0 | 30 | 3 | 0 | 0 | 0 | 0 |
| Starter | 5 | 1 | 150 | 10 | 7,500 | 75,000 | 25 | 250 |
| Growth | 10 | 3 | 500 | 30 | 15,000 | 150,000 | 50 | 500 |
| Pro | 20 | 5 | 1,500 | 100 | 30,000 | 300,000 | 99 | 990 |

Free includes company jobs, the careers page, private candidate records and manual recruitment. It excludes hiring-manager review links, freelancer profiles/new outreach, the Master Talent Pool, meeting integrations, email templates and report exports. Starter adds hiring-manager review links, report exports and freelancer profiles, with outreach to five unique freelancers per UTC calendar month. Repeated outreach to the same freelancer counts once. Messages in existing conversations remain available on every plan. New invitations and first messages enforce the allowance on the server in a database transaction; opening a profile or an empty conversation does not spend it.

Growth adds Master Talent Pool access, email templates, Google Meet and Microsoft Teams interview options, and no monthly freelancer outreach cap. Pro adds Zoom. Provider account authorization remains a later integration phase; the existing manual meeting link flow remains available when interview scheduling is included. Careers application intake already exists in the app and shares the workspace's monthly AI allowance; Employer plans do not introduce a per-position intake cap.

The Clients module remains Agency-only for company owners. Employer subscriptions never enable Agency access. Agency paid plans remain unpublished drafts and retain their separate baseline and Admin approval flow.

Admin maintains all Employer prices, capacities and feature access under **Workspaces & Subscriptions → Manage subscription plans & pricing → Employer plans**. The editor includes Hiring Manager links, freelancer directory access, monthly unique freelancer outreach and Zoom controls. **Apply updated benefits and limits to current subscribers** is optional: it updates active workspaces on that exact plan while retaining their agreed price, renewal date, allowance identity, consumption history, existing records and explicit Admin add-ons. Leave it off to keep existing agreements unchanged. Pending requests and scheduled downgrades retain their original saved snapshots. Reduced limits block new activity when usage is already over the limit.

Approved Employer prices are seeded once. Editing a monthly PKR price recalculates annual and USD amounts using the editor's existing pricing rule; merely opening an editor does not overwrite the approved amounts. Subsequent deployments do not replace saved Admin catalogue changes.

## Usage and isolation

- An application reserves one assessment allowance in the same transaction as CV/application creation. The background assessment reuses that reservation; it is not counted twice.
- Manual assessments/reassessments and JD calls reserve allowance immediately before the provider request. Retries are additional requests. Provider attempts count even when the response fails; no invented score is saved.
- Monthly usage is scoped to the position's owning workspace, including work done by internal/external recruiters. Annual plans still have monthly AI allowances. Usage resets by UTC calendar month. Each plan activation also starts a fresh allocation with only the new plan's limits. Unused AI assessments and JD analyses do not carry forward across plans or months.
- A database write lock serializes quota checks across browsers and background workers. Per-position application caps are durable counters independent of candidate deletion.
- At the per-position careers cap, the listing is unpublished while the position remains active. At monthly AI exhaustion, public listings stop accepting applications; the active positions and stored profiles remain available inside ATS.
- Existing records are retained if a workspace is over its allowance. Creating/activating more positions remains blocked until capacity is available.
- Free Marketplace profiles are excluded from directory results and profile/invitation endpoints. Marketplace visibility follows the active plan automatically, with no separate publishing control. Active, verified Starter and higher accounts are listed even if the legacy profile Published flag is false. Existing private workspace memberships and assigned work are not deleted.
- Access checks apply to services and direct report/document routes. Subscriptions never widen access to another workspace's candidates, clients, documents or messages.

Database startup backs up the existing local database before applying the schema migration. The test suite covers default assignment, pricing revision checks, immutable request quotes, offline payment gating, grants/revocation, monthly and workspace usage separation, concurrent quota spending, career caps, account-specific subscription pages and Admin catalogue authorization.
