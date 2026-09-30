# Collaboration Meetings V1

## Scope

Meeting V1 supports concrete instant and scheduled meetings inside the Collaboration module. It does not add recurrence or reusable templates. Each meeting owns one private conversation containing its internal participants; an external guest never becomes an ERP user or conversation member.

## Lifecycle and roles

- Lifecycle: `SCHEDULED -> LIVE -> ENDED`, or `SCHEDULED -> CANCELLED`.
- Roles: `HOST`, `COHOST`, `PRESENTER`, `ATTENDEE`, `EXTERNAL_GUEST`.
- Internal invitees are admitted automatically. An external invite redeems exactly once into a short-lived access ticket and enters `WAITING` until a Host or CoHost admits it.
- Only Host can grant or remove CoHost authority. Host and CoHost may admit, deny, revoke, lock and end. Host cannot be revoked.
- A locked meeting rejects new guest redemption and admission. A previously admitted, non-revoked guest may reconnect.
- Any participant mutation after `ENDED` or `CANCELLED` fails closed.

## Time and capacity

- Schedule timestamps are canonical UTC `DateTime` values in API and PostgreSQL.
- `POST /api/collaboration/meetings/resolve-jalali-datetime` converts Persian `YYYYMMDD` plus Tehran `HH:mm` through the shared `IPlanningCalendar`; it is protected by `collaboration.meeting.schedule`.
- Capacity is a hard 300 participants. Guest invitations reserve a place. Creation of guest invitations locks the meeting row so concurrent requests cannot exceed capacity.

## Guest security

- Invite and access values are 256-bit cryptographically random opaque tokens.
- Only SHA-256 hashes are persisted. Raw values are returned once and must not be logged or audited.
- Guest invite redemption is protected by an EF concurrency token and unique participant ticket constraint, so concurrent redemption has one winner.
- Guest access tickets are short-lived, revocable, and do not authorize conversation history or files.
- Anonymous guest endpoints expose only title, schedule, status, lock state and guest-safe policy. They use the `meeting-guest` rate-limit policy.

## Media policy

- LiveKit grants are generated server-side. Publish sources are limited to the effective role/policy (`microphone`, `camera`, `screen_share`, `screen_share_audio`).
- Host, CoHost and Presenter may screen-share by default. Attendee and guest media grants come from the meeting policy snapshot.
- External guest file sharing is always false because Meeting V1 has no isolated guest-file API.
- Internal participant identities and external guest identities are stable within a room. A live role change removes the current provider participant before saving the new role, forcing rejoin with a fresh restricted token.
- Revocation invalidates database access first, then removes the provider participant. If provider removal fails, the API reports `MEETING_PROVIDER_REMOVE_FAILED`; access remains revoked and the failed provider operation is audited for retry.
- Ending a meeting deletes the provider room before persisting `ENDED`. Provider failure leaves the meeting `LIVE` and records `MEETING_PROVIDER_END_FAILED`.
- Recording is explicitly disabled in policy, domain and database; Meeting V1 does not claim recording support.

## Attendance and audit

- Clients explicitly call `connected` and `disconnected`; intervals are labelled `CLIENT` and are not presented as provider-certified presence.
- A partial unique index prevents multiple open intervals for one participant.
- Meeting events record lifecycle, waiting-room, role, provider-control and attendance actions.
- Important operations also emit platform audit records without raw invite/access tokens.

## Permissions

The immutable permissions are documented in `docs/PERMISSIONS.md`:

- `collaboration.meeting.view`
- `collaboration.meeting.schedule`
- `collaboration.meeting.start`
- `collaboration.meeting.join`
- `collaboration.meeting.manage_participants`
- `collaboration.meeting.end`
- `collaboration.meeting.invite_guest`
- `collaboration.meeting.report`

## Deliberate limits

- No recurrence or MeetingTemplate in V1.
- No recording.
- No external-guest access to ERP conversations, history or file endpoints.
- Ephemeral whiteboard is a live data-channel capability only; it is not persisted as a document.
