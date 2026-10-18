# Enterprise Collaboration V1 — Verification and UX Audit

**Date:** 2026-09-08  
**Status:** Local development qualified; cross-device network UAT pending

This status intentionally separates application behavior verified on `localhost` from deployment acceptance on the real company network. Collaboration V1, including local audio/video media, is working in the current development environment. It must not be described as fully qualified for LAN, internet or Production until the cross-device criteria in this document pass.

## Verified scope

- Private direct conversations and managed private/public channels
- Message send, edit, soft delete, read state, reactions, and Hadron stickers
- File/image upload with size, extension, MIME signature, SHA-256, and private download authorization
- SignalR updates and automatic reconnect behavior
- Formal letters with Draft, Issued, Acknowledged, and Closed states
- Immutable issued-letter content and append-only lifecycle history
- Jalali `YYYYMMDD` response-due input with canonical Gregorian storage and visible Gregorian equivalent
- LiveKit voice and video connection on `localhost`, including actual local media publication
- Microphone mute/unmute, camera off/on, end call, and rejoin behavior
- Browser/deployment preflight before creating or joining a call
- Per-connection LiveKit participant identity without losing the authenticated ERP user mapping
- Database enforcement of at most one active call per conversation
- Cleanup of a newly created call when its media bootstrap fails
- Permission metadata on every Collaboration endpoint and backend fail-closed authorization
- Audit events for create, update, upload, transition, and delete operations
- Persian RTL and English LTR presentation in the canonical Hadron application shell

## Current automated evidence

| Check | Result |
|---|---:|
| Frontend automated suite | 203/203 passed |
| Focused call-support scenarios | 13/13 passed |
| Backend focused call-hardening scenarios | 5/5 passed |
| Full backend integration suite | 392/392 passed |
| Frontend lint | Passed |
| Frontend and backend builds | Passed |
| Collaboration migration on the local database | Applied |

The LiveKit client remains a separate lazy chunk, so ordinary ERP pages do not pay the media-library startup cost.

## Human-in-the-loop evidence

The development application was used in a real browser as an authenticated administrator. Earlier messaging and correspondence checks remain represented by the private test workspace; the call flow was re-tested after the hardening changes.

The following call interactions completed successfully on `localhost`:

1. Start a voice call and grant browser microphone permission.
2. Publish the microphone track and reach the connected voice-call state.
3. Mute and unmute the microphone.
4. End the voice call and verify that it is no longer shown as active.
5. Rejoin/start the next call without being trapped by stale call state.
6. Start a video call and grant browser camera permission.
7. Publish audio and video and display the local video preview.
8. Turn the camera off and on.
9. End the video call and verify cleanup in both the overlay and conversation state.
10. Open the same conversation in two authenticated browser tabs, receive the live call-start event in the second tab without refresh, and join the same room as two distinct LiveKit participants.
11. End the call in the first tab and verify that the second tab closes its call overlay from the realtime end event without refresh.
12. Query the local database after cleanup and verify that no `ACTIVE` call session remains.

This is evidence of a functioning browser-to-LiveKit media path on the application host. It is not evidence of a functioning route between two physical client devices.

## Call hardening implemented

### Browser and signaling preflight

Before the backend creates or joins a call, the frontend checks all of the following:

- provider configuration is available;
- the page is a secure browser context;
- `navigator.mediaDevices.getUserMedia` is available;
- the signaling URL is a valid `ws://` or `wss://` URL;
- an HTTPS page is never paired with insecure `ws://` signaling.

The Calls page reports the same effective readiness rather than showing “ready” solely because server-side credentials exist. Production backend configuration also rejects insecure WebSocket signaling.

### Per-connection participant identity

Each join token uses a unique connection identity composed from the immutable ERP user ID and a newly generated connection ID. The opaque ERP user ID and connection ID are also carried in participant metadata. This preserves authorization and traceability while preventing a second browser/tab for the same authenticated user from being rejected as a duplicate LiveKit participant.

### One active call per conversation

A filtered unique database index, `ux_call_sessions_one_active_per_conversation`, enforces at most one `ACTIVE` call for each conversation while preserving ended-call history. Concurrent start requests are reconciled against that database authority and return the winning active session instead of creating parallel rooms or leaking a provider-independent server error.

### Ghost-call cleanup

`StartCall` returns the explicit `createdNew` ownership flag. If signaling or initial microphone/camera publication then fails before media is established, the frontend ends the backend session only when that flag proves the current request created it, and refreshes the conversation. A participant who merely receives or joins an existing call does not end the shared call for everyone else. This prevents both a failed bootstrap from leaving a false “active call” and an unsuccessful join from destroying another user's call.

The client also handles reconnect/reconnected/disconnected states, stops local tracks during cleanup, and offers an explicit audio-playback action when browser autoplay policy blocks remote sound.

### Realtime Hub contract and retry

The SignalR wire methods `JoinConversation` and `LeaveConversation` accept only `conversationId`. Cancellation is no longer exposed as a client-supplied Hub argument; the Hub uses `Context.ConnectionAborted` internally. This keeps the JavaScript invocation contract aligned with the server and prevents binding errors caused by an extra wire parameter.

The frontend retries the initial SignalR start and retries conversation-group join after reconnect. A successful reconnect therefore re-establishes the authorized conversation subscription instead of leaving the page visually connected but no longer receiving conversation events.

## Character and sticker asset audit

The supplied Hadron character reference remains the design authority. Seven optimized transparent WebP stickers are available for explicit user-authored communication: Hello, Done, Checking, Wait, Sending, Sorry, and Great. They are not used to represent API success, approval, security state or other system truth.

## Operational boundary and remaining acceptance

The earlier statement that actual microphone/camera traffic was disabled is no longer accurate: voice and video were successfully published on `localhost`. The remaining limitation is specifically cross-device deployment and network acceptance.

The meeting-specific local qualification, including the guest waiting room, secure invitation, meeting chat, whiteboard, attendance report, and provider-side room deletion, is recorded in [MEETINGS-V1-AUDIT.md](MEETINGS-V1-AUDIT.md).

The module is **not yet fully qualified for company LAN, internet or Production calling**. Provider-side `DeleteRoom` is now implemented and was verified against the real local provider. Production lifecycle hardening still requires a LiveKit webhook or idempotent empty-room reaper so browser crashes and abrupt disconnects cannot leave an ERP call session stale. Final acceptance requires:

1. ERP UI served through trusted HTTPS on the client devices;
2. LiveKit signaling exposed through trusted `wss://`;
3. the Caddy permissions policy allowing `camera=(self)` and `microphone=(self)`;
4. `LIVEKIT_NODE_IP` resolving/routing to the media host from the actual client network;
5. firewall access to LiveKit media UDP range and TCP fallback;
6. two physical devices using two distinct ERP accounts;
7. bidirectional audio and video, mute/unmute, camera toggle, end and rejoin tested across those devices;
8. TURN/TLS deployment and validation before claiming reliable access from restricted, VPN or external networks.

Until all eight checks pass, the accurate status is “localhost qualified; cross-device UAT pending.” No call recording exists in v1.
