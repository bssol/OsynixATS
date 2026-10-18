# Freelancer profiles, assignment invitations and Messages

Employer and Agency owners start in **My Team → Find Freelance Recruiters → View Profile**. The profile uses the published professional portfolio (overview, skills, technologies, industries, experience, education, case studies, availability and working preferences). Private account contact details and recruitment data are excluded. Reviews remain empty until a verified review process exists.

**Invite To Project** opens a position invitation or a general project brief. Position options include only open positions belonging to the selected Employer/Agency working profile. Sending creates a private conversation. The same freelancer may receive further assignments, including when already in the external team. Duplicate pending invitations for the same position/brief are rejected.

Freelancers open **Messages**, directly below **My Tasks** in the sidebar. Inbox, unread filters, search, invitation cards and replies share one conversation with the sender. The header notification bell and sidebar show unread counts. Reading a thread clears its unread count. New messages and responses refresh connected sessions using the existing application update service.

Freelancers can accept, decline or ask a question. Questions keep the invitation pending. Employers can cancel pending invitations. Accepting an open-position invitation creates an external membership if necessary and assigns that position; it consumes neither an internal team seat nor a position slot in the freelancer's personal workspace. General briefs create the external relationship without assigning a vacancy. Deactivated memberships cannot be reactivated by accepting an invitation. Closed/filled positions and revoked Agency access cannot be accepted.

Messages support text and downloadable PDF, DOCX, PNG/JPEG attachments up to 5 MB. Downloads check both the signed-in account's current access and conversation participation. Files are served as attachments with no caching and MIME sniffing disabled. Messages/invitation records persist separately from candidate data. Commercial terms and payments are negotiated by the parties outside the ATS.

Email notification is optional. In-app delivery works locally. Email requires a configured mail provider and public HTTPS `Review:PublicBaseUrl`. The invitation card reports whether email was sent or unavailable; unavailable email does not discard the in-app invitation.

## Verification

- Employer/Agency: open a published profile, review every section, send a position invitation and a project brief, then return through Messages.
- Freelancer: observe notification badge, read the invitation, ask a question, then accept or decline. Confirm the Employer sees the same thread/status.
- Accept a position invitation and open the existing Employer workspace; only the assigned vacancy becomes available. Internal seat and personal slot allowances must remain unchanged.
- Try a different user's conversation/attachment URL; access must fail. Deactivate membership or close a vacancy before acceptance; acceptance must fail.
- Automated tests: `MarketplaceMessagesTests`, the profile/inbox cases in `HttpWorkflowTests`, and `WorkspaceAccountTests`.

The `MarketplaceMessaging` EF migration creates conversations, messages and invitations with workspace/participant constraints and indexes. The normal startup migration process backs up existing SQLite data before upgrading.
