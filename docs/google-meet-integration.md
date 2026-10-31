# Google Meet interview integration

Google Meet connects separately from Google sign-in. Each organizer authorizes their own Google account. The integration creates events in that account's primary calendar and requests a unique Meet conference, sends Google Calendar invitations, updates approved interview reschedules, and cancels meetings when an interview is cancelled or switched to another provider/on-site.

## Administrator setup

1. In Google Cloud, enable **Google Calendar API** and **Gmail API** for your project.
2. Configure the OAuth consent screen, audience and permitted test users (while in testing). Request `https://www.googleapis.com/auth/calendar.events.owned` and `https://www.googleapis.com/auth/gmail.readonly`, plus Google's basic profile/email scopes. Gmail read-only is a restricted scope. Complete Google's applicable OAuth verification and security assessment requirements before general release.
3. Create a **Web application** OAuth client. Register the exact redirect URI `https://YOUR-ATS-HOST/signin-google-meet`. For local development also register `http://localhost:5080/signin-google-meet` if that is your actual local URL.
4. Store `GoogleMeet:ClientId` and `GoogleMeet:ClientSecret` in development user secrets or the host secret manager (`GoogleMeet__ClientId` / `GoogleMeet__ClientSecret` environment variables). Never commit a client secret. This client can be separate from the existing Google sign-in client.
5. Configure the existing `Review:PublicBaseUrl` and email delivery so candidate ATS response links are usable. Persist ASP.NET Core Data Protection keys and the application database across restarts.
6. Build and restart the application. Normal startup applies the new EF migration with the existing database backup process. Do not run two application instances with SQLite.

Open **Settings → Integrations → Google Meet**, then connect your Google account and grant Calendar plus Gmail read-only access. The Gmail permission lets ATS search Google Calendar proposal notification emails; ATS does not send or delete Gmail messages. Return to the interview form, select **Google Meet**, review the invitation and send. Do not paste a link for automatic creation; existing manually pasted links remain supported.

## Reliability and data

Refresh credentials are protected with ASP.NET Core Data Protection in Identity user tokens. Access tokens are requested server-side and are never returned to the browser. Calendar event IDs are persisted separately, with deterministic IDs to avoid duplicates after uncertain API responses. A background worker retries pending synchronization each minute while the application is running; the interview panel also supports manual retry. ATS invitation emails are held when Google synchronization fails or conference creation is pending. Google's own calendar invitation and the ATS response email are separate messages. Google Calendar Yes/No/Maybe responses are imported each minute while the app runs; Calendar proposal emails are searched for a matching iCalendar COUNTER by event UID and candidate address. Only the proposed time, time zone, candidate comment, and a message fingerprint are stored in ATS. **Check Google responses** checks immediately.

Reconnect the original Google account if authorization expires. Switching to another Google account is blocked while linked events remain. Disconnect is blocked until active interviews are cancelled and synchronized; disconnect removes the application's stored credential. Users may separately revoke the app in their Google Account permissions.

## Validation

Automated HTTP-fixture tests cover creation, generated links in email, pending conference creation, failure/retry, duplicate prevention, approved reschedules, cancellation, protected credentials and account ownership. They make no live Google calls and send no real emails.

Live acceptance requires a configured OAuth client, enabled Calendar and Gmail APIs, and a consenting organizer account. Verify authorization, one interview with test attendees, candidate confirmation, a candidate-proposed new time imported from Gmail, approval in ATS, cancellation and reconnect after revoking access. Calendar availability checks and Teams and Zoom authorization are separate future work.

Reference: https://developers.google.com/workspace/calendar/api/guides/create-events

## This local installation

Use `http://localhost:5080/signin-google-meet` as the authorized redirect URI for the requested local test setup. After configuring credentials and restarting, open `http://localhost:5080/settings/integrations?provider=google-meet`.

Development secret commands (replace placeholders locally; do not paste secrets into chat):

```powershell
dotnet user-secrets set "GoogleMeet:ClientId" "YOUR-GOOGLE-OAUTH-CLIENT-ID" --project src/Osynix.Ats
dotnet user-secrets set "GoogleMeet:ClientSecret" "YOUR-GOOGLE-OAUTH-CLIENT-SECRET" --project src/Osynix.Ats
```

Google Calendar sends its own attendee invitations even when ATS email delivery is disabled for local testing. Use test attendee addresses you control.

## Verification recorded for this change

- Build succeeds (three existing nullable warnings in `Candidates.razor`).
- Final OAuth and interview workflow run: **41 passed, 0 failed**; includes 10 new Google integration/security cases.
- Broader regression run: 226 passed, 35 failed. An untouched HEAD archive reproduced 34 of those failures in a full run. The remaining CV-profile worker test also failed when rerun against untouched HEAD in isolation; its shared SQLite connection/background timing is unstable. These broader failures are outside the Google integration scope.
- No live OAuth consent, Google Calendar calls or actual attendee emails were performed during verification. The existing running app must be rebuilt/restarted after Google Cloud setup.

## Candidate Google Calendar responses

Yes confirms the ATS interview; No creates a candidate response review task. Maybe creates a Response Requires Review status and a Review Interview Response task. These appear in the assigned recruiter's notification bell, Action Needed/Outstanding Tasks and the application's Interview panel. Workspace owners can also see their team's review tasks and notifications; unrelated workspaces remain excluded.

Google's Calendar Events API exposes attendee responseStatus and comment, but not a proposed-new-time field. A Maybe RSVP alone is not treated as an exact proposal. ATS searches the organizer's Gmail for Google's Calendar proposal notification and matches the iCalendar COUNTER UID and candidate address to the existing interview. Once imported, the exact proposed time appears in the interview panel with **Accept proposed time** and **Suggest another time** actions. Accepting in ATS updates the existing Google Calendar event and sends the new invitation update. Google Calendar's **Event responses** notification must be set to **Email** for Gmail import to work; without a matching notification, the interview remains in Response Requires Review.

The original schedule stays unchanged until approval. Only the configured candidate attendee is imported; manager responses are not mistaken for candidate confirmation. Fingerprints prevent repeated polling from duplicating notifications or replacing a manually recorded proposal with an unchanged Google response. Existing guest responses are preserved when patching an event with the same guest list. Candidate response tokens remain private rather than being embedded in shared calendar descriptions.

The GoogleCalendarCandidateResponses and GoogleCalendarProposalInbox migrations are applied through the normal startup backup/migration process. Reconnect Google Meet after deployment to grant the additional Gmail read-only scope and import existing proposal notifications.

API reference: https://developers.google.com/workspace/calendar/api/v3/reference/events

Inbound response verification: 48 targeted interview/OAuth tests passed, including candidate-only RSVP matching, duplicate polling, revoked sharing, exact proposal recording and approval, proposal-review authorization, and preservation of existing guest responses. The updated local build is running at http://localhost:5080 and the GoogleCalendarCandidateResponses migration was applied successfully through normal startup.

## Credential configuration and refresh errors

Dedicated `GoogleMeet:ClientId` and `GoogleMeet:ClientSecret` settings take precedence. If neither is configured, the integration uses the saved `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` pair. Both Google sign-in and Meet can use that OAuth client, provided its authorized redirect URIs include `/signin-google-meet`. A partially configured dedicated Meet client is treated as incomplete and is never mixed with the other client's secret.

Missing client settings, rejected client credentials, expired/revoked authorization, and a temporary token-service failure now have different messages. Only an expired/revoked grant or an OAuth-client change calls for reconnecting the original Google account. A temporary Google failure calls for retrying. Reconnection is performed through Settings → Integrations → Google Meet; it does not delete existing interview meetings.

## Rejected stored authorization (401 unauthorized_client)

The live diagnostic identified Google's `unauthorized_client` response for the saved refresh grant. This is not a temporary outage. Reconnect Google Meet from Settings → Integrations using the same connected Google account so Google issues authorization for the currently configured OAuth client. Existing meetings are preserved.

New saved connections record the OAuth client identifier inside the protected credential payload. A later client change is detected before attempting to reuse that refresh token. Successful reconnection clears stale authorization errors while leaving unrelated calendar errors intact. Check connection verifies authorization without creating, changing or cancelling a meeting.

For an administrator-only local diagnostic, run the app with `--verify-google-meet` under the same Development environment/content directory. It exchanges refresh credentials only, reports HTTP/error codes, prints no credentials or access tokens, and does not modify calendar events or the database. A successful check reports `GOOGLE_MEET_AUTH_OK`.
