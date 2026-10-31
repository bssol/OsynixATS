# Settings: Integrations

Open Settings → Integrations (`/settings/integrations`). This is the central catalogue for provider connections. New integration providers and categories belong in `IntegrationSettingsService.Catalogue` rather than standalone settings links.

## Subscription access

The active workspace's effective subscription terms control access. Employer and freelance workspace owners see an enabled Integrations tab only when `MeetingIntegrations` is included. Active team members may connect their own organizer accounts under the same workspace entitlement. Without access, the tab is disabled and direct page navigation shows the subscription requirement. Administrator accounts retain setup/testing access.

Zoom additionally respects `ZoomIntegration`. The current catalogue contains Google Meet, Microsoft Teams and Zoom. Only Google Meet account connection is implemented; the others are marked Coming soon (or Not included when the plan excludes them).

Connect requests and OAuth callbacks independently revalidate the account, workspace and plan. A downgrade during authorization cannot create a connection. A freelancer's paid personal plan cannot unlock integrations for a different workspace without that entitlement.

## Google Meet

Choose the Google Meet card, then Connect Google Meet or Manage connection. Connected account details, existing authorization errors, Reconnect and Disconnect are presented within the Integrations settings tab. Connection credentials remain personal to the organizer; another workspace member's credentials are not shared.

The previous `/integrations/google-meet` page redirects to the hub. Its POST handlers remain for secure authorization and compatibility. The Google Cloud callback remains `/signin-google-meet`; no redirect URI change is required. OAuth success/failure returns to the Google Meet panel in Settings. Existing saved connections and meetings are preserved.

Interview forms link to Settings → Integrations for connection management. Connecting a provider does not schedule an interview or send attendee emails. Those actions remain in the interview workflow.

The existing Google authorization/token-service issue is deferred while this settings flow is introduced. It is displayed as Needs attention, not as a successful healthy connection.

Validation: build succeeds; 59 targeted interview, OAuth and settings tests passed. New cases cover enabled/disabled employer and freelancer plans, direct connect-route enforcement, downgrade during OAuth, legacy redirects, credential privacy and cross-workspace entitlement isolation.
