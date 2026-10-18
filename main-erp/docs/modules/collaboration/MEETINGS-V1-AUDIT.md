# Collaboration Meetings V1 — Final Local Qualification

**Date:** 2026-09-08  
**Environment:** local development (`localhost`, PostgreSQL, ASP.NET Core, React/Vite, LiveKit 1.9.6)  
**Result:** qualified for local development; physical multi-device and production-network UAT remain required

## Scope verified

- Instant and scheduled meetings in the Collaboration workspace
- `SCHEDULED -> LIVE -> ENDED` and direct instant `LIVE -> ENDED` behavior
- Host, CoHost, Presenter, Attendee, and External Guest roles
- Jalali scheduling input with canonical UTC persistence
- Internal-member selection, role assignment, and meeting update
- Opaque single-use external invitation in the URL fragment
- Waiting room, admission, denial/revocation, lock/unlock, and guest reconnect policy
- LiveKit audio/video room connection and server-side publish-source grants
- Meeting chat, participant panel, ephemeral whiteboard, screen-share entry point, and persistent minimized dock
- Independent leave versus host end-for-everyone behavior
- Client-labelled attendance intervals and report display
- Backend permissions, fail-closed endpoint protection, provider failure behavior, platform audit, and database constraints

## Automated evidence

| Check | Result |
|---|---:|
| Frontend lint | Passed |
| Frontend automated suite | 203/203 passed |
| Frontend production build | Passed |
| Focused LiveKit provider-token tests | 3/3 passed |
| Full backend integration suite | 392/392 passed in 13m 12s |
| Meeting migrations | Applied to the local development database |

The LiveKit client is emitted as a separate lazy-loaded chunk. The production build reports a non-blocking chunk-size warning for that media-only chunk; ordinary ERP pages do not load it eagerly.

## Human-in-the-loop scenarios

The following checks were performed through the real browser UI against the local database and local LiveKit provider.

### Discoverability correction — 2026-09-09

The earlier meeting scenarios proved the workflow after entering meeting routes, but they did not prove that a normal user could discover those routes from the Messages page or from another ERP workspace. The owner correctly identified this gap. The earlier direct-route check must not be represented as a complete end-to-end human UX review.

The correction was re-tested from the actual authenticated Messages page without typing a route:

1. The page header exposes a labelled `Meetings and calls` action.
2. A selected conversation exposes the effective permission-aware `Start instant meeting` or `Schedule meeting` action instead of an icon-only control.
3. A persistent, labelled Communication dock opens from the application shell and exposes permission-aware deep links to Messages, Meetings, New/Instant Meeting and Formal Letters.
4. The dock opens and closes as a disclosure, and its filters, empty state and quick actions are present in the accessibility tree.
5. The `Meetings and calls` quick action was followed naturally to the meeting worklist; the list, filters, refresh action, four existing rows and named row actions were visible.
6. One meeting Object Page was opened and returned to the list; the Schedule Meeting form and its Scheduled/Instant mode switch were then reached without submitting a record.
7. A Persian title search returned the expected meeting; full UUID and the displayed eight-character UUID prefix are covered by a real PostgreSQL integration test.
8. A multi-word Persian meeting title was typed continuously in Instant mode and the action became enabled without submitting the form.
9. Opening the global dock moved focus to its first useful link; `Escape` closed it and restored focus to the launcher.

Post-correction regression evidence: frontend `253/253`, focused Collaboration/Meeting integration `21/21`, lint and production build passed.

This is a bounded desktop browser navigation and readability check. It is not a keyboard-only, NVDA, 200% zoom, mobile-device, media-quality or multi-person human usability sign-off.

### Scheduled meeting and external guest

1. Created a scheduled meeting with Host, CoHost, and Presenter members.
2. Entered the date in Jalali format, verified the Gregorian equivalent, and edited the saved meeting.
3. Verified continuous time-field typing instead of one-character/re-click behavior.
4. Generated a one-use guest invitation and verified that the credential stayed in the URL fragment rather than the query string.
5. Opened the guest shell and verified that ERP navigation and authenticated workspace data were absent.
6. Redeemed the invitation, entered the waiting room, admitted the guest as Host, and observed automatic guest transition to prejoin.
7. Joined the same LiveKit room from authenticated-host and guest browser tabs.
8. Verified participant presence with cameras off, bidirectional participant-list state, microphone toggles, and camera toggles.
9. Sent host-to-guest and guest-to-host meeting-chat messages.
10. Drew eight whiteboard segments, observed the same eight segments in both tabs, then cleared the board and observed zero segments in both tabs.
11. Minimized the meeting, navigated to another Collaboration page, and restored it from the persistent global dock.
12. Locked and unlocked the meeting, revoked the guest, and verified the guest disconnect state.
13. Ended the meeting for everyone and verified provider room deletion, ERP `ENDED` state, locked terminal state, and the guest-facing ended message.
14. Reused the consumed invite in a fresh tab and verified rejection as invalid, expired, or already used.
15. Verified list search, empty result, matching result, and `ENDED` status filter.
16. Opened the attendance report and verified separate host/guest intervals labelled `CLIENT`.

### Instant meeting

1. Created an instant meeting from the UI and verified immediate `LIVE` state.
2. Entered prejoin with microphone and camera disabled and joined successfully without publishing media.
3. Verified the identity fallback tile, one-person participant panel, waiting-room empty state, and meeting chat availability.
4. Used ordinary Leave and verified that the meeting remained `LIVE` and joinable.
5. Verified that the attendance report closed the interval and displayed its duration.
6. Used End for everyone and verified terminal `ENDED` state and a second successful provider-side `DeleteRoom` response.

## Defects found and corrected during the audit

| Defect | Correction | Regression evidence |
|---|---|---|
| LiveKit rejected `DeleteRoom` with HTTP 401 | The server now issues the operation-specific `roomCreate` administration grant; participant removal retains the room-scoped `roomAdmin` grant | Focused token test plus two real provider `DeleteRoom` responses with status 200 |
| A participant joining with camera off could be absent from the stage | A stable identity tile is rendered for every participant without visible camera media | Re-tested in the instant meeting with both devices disabled |
| Reconnect/HMR could leave duplicate or orphan media wrappers | Media wrappers are keyed by participant/media kind and removed as one unit during detach/cleanup | Clean reconnect and DOM tile-count check |
| Whiteboard CSS also enlarged the clear-button icon | Canvas rules are now scoped to the dedicated whiteboard-canvas class | Two-tab draw/clear visual test |
| Provider disconnects surfaced as a generic network failure | LiveKit disconnect reasons now map to ended, revoked, duplicate-identity, or network states | Guest revoke and host end states re-tested |
| Muted microphone/camera controls exposed inverted `aria-pressed` state | The accessible pressed state now matches the actual enabled state | Source correction, lint, automated suite, and production build |
| Ending from the full-screen room could leave the underlying detail/list view stale until its next poll | A context revision now refreshes visible meeting read models immediately after a successful global end | Re-tested from the in-room end action |

## Security and data conclusions

- Raw guest invite and guest access values are not persisted; only hashes are stored.
- Anonymous guest endpoints expose only guest-safe meeting data and use the guest rate limiter.
- Guest access does not create an ERP identity and does not expose the internal conversation or files.
- Recording remains deliberately unavailable in V1 and cannot be enabled by an untrusted policy payload.
- Provider termination is performed before persisting the ERP terminal state. A provider failure therefore leaves the meeting recoverably `LIVE` instead of claiming a false successful end.
- Attendance is explicitly labelled `CLIENT`; it is not represented as provider-certified payroll or legal attendance.

## Acceptance boundary

This audit proves the application flow on two browser tabs and one physical Windows computer. It does **not** yet prove production-quality media across two physical devices or external networks.

Before Production acceptance, complete all of the following on the target deployment:

1. Serve the ERP over trusted HTTPS and LiveKit over trusted WSS.
2. Configure and verify the advertised LiveKit node address, UDP media range, and TCP fallback from real client devices.
3. Add and validate TURN/TLS for restrictive, VPN, mobile, and internet networks.
4. Test two physical devices with two distinct ERP accounts, plus one anonymous external guest.
5. Verify bidirectional audio/video and an actual selected screen source; the local audit reached the native browser screen-source picker but intentionally did not choose a private desktop/window.
6. Test disconnect/reconnect during a real network interruption and verify attendance intervals.
7. Add the provider webhook or idempotent empty-room reaper required for browser-crash cleanup before Production rollout.
8. Keep recording disabled until storage, consent, retention, permission, and audit requirements are separately approved.

No Production, LAN, TURN, mobile-device, or multi-physical-device claim is made by this local qualification.
