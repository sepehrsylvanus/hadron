# Hadron Enterprise Collaboration v1

**Status:** Implemented; localhost validated; cross-device UAT pending  
**Workspace:** ارتباطات سازمانی  
**Module owner:** Collaboration

## Business scope

This module provides a private enterprise communication surface inside `Hadron ERP System` without granting collaboration users authority over MRP, Inventory, Procurement, Cash Flow or other business modules.

Version 1 includes:

- public/private channels and direct conversations;
- text messages, replies, reactions, editing and audit-preserving soft deletion;
- validated image/document/audio/video attachments in private S3-compatible storage;
- formal internal correspondence with immutable issued records;
- self-hosted LiveKit voice/video rooms with short-lived, room-scoped tokens;
- Hadron character stickers as user-authored message content only.
- a permission-aware global Communication Center with unread counters, actionable correspondence, active-call visibility, deep links, realtime refresh and a polling fallback.

Presence, message search, recording, transcription, external federation, bots and ERP-record linking are deferred.

## Ownership and persistence

The Collaboration module owns schema `collaboration`, its EF mappings, API contracts, permissions and tests. It may read the active Identity user directory through a bounded query. It must not update Identity or any operational module table.

Attachments are metadata rows in PostgreSQL plus private objects in the configured MinIO bucket. Upload accepts at most 25 MB, verifies the allowed MIME type against file magic bytes, computes SHA-256 and strips directory content from client filenames. Executables are rejected. Database metadata is authoritative; storage leases are completed only after persistence succeeds.

## Conversation rules

- A direct conversation is private and its participants are fixed by the direct-conversation use case.
- A private channel is visible only to members and System Administrators.
- Channel membership and archive operations require the channel-management permission and owner policy.
- Text and stickers are user content. Only text is editable.
- A user may edit/delete their own message; moderators may act on other messages.
- Delete is a soft delete. System, letter-reference and call-event messages are immutable.
- Optimistic versions protect mutable aggregates. Stale writes return localized `409` Problem Details.
- SignalR events refresh connected clients. The client retries initial connection, uses automatic reconnect, retries conversation-group join after reconnect, and then performs an authoritative API reload.
- The SignalR wire contract for `JoinConversation` and `LeaveConversation` accepts only `conversationId`; cancellation is derived internally from `Context.ConnectionAborted`, not supplied as a client argument.
- Every authenticated Collaboration connection joins only its own user-scoped notification group. Conversation payloads are still authorized and filtered by the backend.

## Formal correspondence

Lifecycle:

```text
DRAFT -> ISSUED -> ACKNOWLEDGED -> CLOSED
              \--------------------/
```

- Drafts may be edited only by the sender.
- Issue requires at least one `TO` recipient.
- Issued letters are immutable; corrections require a new letter.
- A recipient acknowledges; the sender closes.
- Number, sender/recipient snapshots, lifecycle events and timestamps remain queryable.
- Attachments may be added only while the sender's letter is Draft.

This is internal operational correspondence, not qualified digital signature or statutory document management.

## Calls

The API creates a call session only when LiveKit configuration is valid. Join tokens are HS256-signed, room-scoped, identity-scoped and expire after two hours. Each token receives a unique per-connection participant identity while participant metadata retains the immutable ERP user ID for traceability. This permits multiple legitimate connections by one authenticated user without LiveKit duplicate-identity replacement.

The browser runs a preflight before creating or joining a call: provider availability, secure browser context, media-device API availability, valid WebSocket URL and secure `wss://` signaling for HTTPS pages. It publishes only after explicit microphone/camera permission.

The database permits at most one active call per conversation through a filtered unique constraint while retaining ended-call history. Concurrent starts resolve to the database-backed winning session. The `StartCall` response carries `createdNew`, which identifies whether that request created the returned session or received the already-active session. If initial signaling or local-media publication fails, the client ends the session only when `createdNew` is true and then refreshes the conversation, so a ghost active call does not block retry. A failed attempt to join someone else's existing call never ends that shared call.

The client supports reconnect state, remote media attachment, explicit audio-playback recovery, microphone mute/unmute, camera off/on, leave and authorized end. No call recording exists in v1.

LiveKit API key and secret are deployment secrets and must never be committed or returned to the browser. Only the signed join token is returned.

## Permissions

- `collaboration.workspace.view`
- `collaboration.channel.manage`
- `collaboration.message.send`
- `collaboration.message.moderate`
- `collaboration.file.share`
- `collaboration.call.join`
- `collaboration.call.start`
- `correspondence.letter.view`
- `correspondence.letter.manage`
- `correspondence.letter.issue`
- `correspondence.letter.acknowledge`

Every `/api/collaboration` endpoint and the SignalR hub require registered permission metadata. UI visibility never substitutes for backend enforcement.

## Date standard

Official-letter response deadlines follow the ERP date standard. Persian UI accepts Jalali `YYYYMMDD`, shows the Gregorian equivalent, and sends a canonical Gregorian `date` to the backend. English UI uses Gregorian date input.

## Character/sticker rule

Stickers are sent only through an explicit user action and are stored as a stable sticker code. They must not represent API success, financial approval, security status, workflow completion or system truth. The UI maps the code to approved transparent Hadron assets and includes a text alternative.

## Operational acceptance

- migration applied in Development/UAT before enabling routes; the current migration has been applied to the local development database;
- dedicated MinIO collaboration bucket included in backup/restore;
- HTTPS/WSS configured before microphone/camera UAT;
- LiveKit UDP/TCP paths validated from the real client network;
- bidirectional voice/video, controls, end and rejoin validated with two physical devices and two distinct ERP accounts;
- permission matrix and private-channel isolation verified;
- retention/legal-hold policy approved before production use of formal correspondence.

Voice/video, mute, camera toggle, end and rejoin have passed human testing on `localhost`. This proves the local application/media path only. Company-LAN, external-network and Production calling must not be labeled fully qualified until trusted HTTPS/WSS, firewall routes and the two-device/two-account UAT above are complete.
