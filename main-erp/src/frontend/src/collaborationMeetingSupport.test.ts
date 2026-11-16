import assert from 'node:assert/strict'
import test from 'node:test'
import {
  activeMeetingDetailPollingProfile,
  buildGuestMeetingInviteRoute,
  buildGuestMeetingInviteUrl,
  buildMeetingDetailRoute,
  extractMeetingInviteToken,
  guestMeetingStatusPollingProfile,
  guestMeetingRoute,
  isGuestAccessTicketExpired,
  isMeetingCreateSubmitDisabled,
  isMeetingAdmissionStatus,
  isMeetingRole,
  isMeetingStatus,
  isValidMeetingInviteToken,
  meetingAdmissionTone,
  meetingAdmissionTranslationKey,
  meetingCapabilitiesForRole,
  meetingCreateHeaderKeys,
  meetingLifecycleActions,
  meetingListRoute,
  meetingLocalDateTimeToUtc,
  meetingPollingDelayMs,
  meetingRequestErrorText,
  meetingRequestHttpStatus,
  meetingRoleTone,
  meetingRoleTranslationKey,
  meetingStatusTone,
  meetingStatusTranslationKey,
  meetingUtcToLocalDateTime,
  newMeetingRoute,
  normalizeMeetingLocalDateTimeInput,
  shouldDiscardGuestMeetingTicket,
  shouldShowMeetingListFailure,
  MeetingRequestGenerationGate,
  type MeetingPolicy,
} from './collaborationMeetingSupport.ts'
import { GuestMeetingRequestGate, scrubGuestMeetingCredentialFromUrl } from './guestMeetingFlowSupport.ts'

const policy: MeetingPolicy = {
  attendeeMicrophone: true,
  attendeeCamera: false,
  attendeeScreenShare: false,
  guestMicrophone: false,
  guestCamera: false,
  guestScreenShare: false,
  chatEnabled: true,
  fileSharingEnabled: true,
  ephemeralWhiteboardEnabled: true,
  recordingEnabled: false,
}

const validId = '01993b86-34c6-7a17-8fb5-f6ccbdc19a22'
const validInvite = 'eJ2GHG1B6dFkB-q2E-RH4C7sVFdZeA5pFw_XKfHCl3Q'

test('meeting create header follows the selected scheduled or instant workflow', () => {
  assert.deepEqual(meetingCreateHeaderKeys('SCHEDULED'), {
    title: 'collaborationMeetings.createScheduledTitle',
    subtitle: 'collaborationMeetings.createScheduledSubtitle',
  })
  assert.deepEqual(meetingCreateHeaderKeys('INSTANT'), {
    title: 'collaborationMeetings.createInstantTitle',
    subtitle: 'collaborationMeetings.createInstantSubtitle',
  })
})

test('scheduled meeting submit stays disabled until its date is resolved without error', () => {
  const ready = {
    busy: false,
    title: 'Quarterly planning',
    mode: 'SCHEDULED' as const,
    date: '14050618',
    resolvedDate: '2026-09-09',
    dateError: '',
  }

  assert.equal(isMeetingCreateSubmitDisabled(ready), false)
  assert.equal(isMeetingCreateSubmitDisabled({ ...ready, date: '' }), true)
  assert.equal(isMeetingCreateSubmitDisabled({ ...ready, resolvedDate: '' }), true)
  assert.equal(isMeetingCreateSubmitDisabled({ ...ready, dateError: 'Invalid date' }), true)
  assert.equal(isMeetingCreateSubmitDisabled({ ...ready, title: '   ' }), true)
  assert.equal(isMeetingCreateSubmitDisabled({ ...ready, busy: true }), true)
})

test('instant meeting submit ignores stale scheduled-date state but still requires title and idle form', () => {
  const instant = {
    busy: false,
    title: 'Operations stand-up',
    mode: 'INSTANT' as const,
    date: '',
    resolvedDate: '',
    dateError: 'Stale scheduled-date error',
  }

  assert.equal(isMeetingCreateSubmitDisabled(instant), false)
  assert.equal(isMeetingCreateSubmitDisabled({ ...instant, title: '' }), true)
  assert.equal(isMeetingCreateSubmitDisabled({ ...instant, busy: true }), true)
})

test('meeting request generations reject stale responses across replacement, leave, and same-id reactivation', () => {
  const gate = new MeetingRequestGenerationGate()
  const inactive = gate.capture()
  const first = gate.transition('meeting-a')
  assert.equal(gate.isCurrent(inactive), false)
  assert.equal(gate.isCurrent(first), true)
  assert.equal(gate.currentKey(), 'meeting-a')

  const replacement = gate.transition('meeting-b')
  assert.equal(gate.isCurrent(first), false)
  assert.equal(gate.isCurrent(replacement), true)

  const left = gate.transition(null)
  assert.equal(gate.isCurrent(replacement), false)
  assert.equal(gate.isCurrent(left), true)

  const firstActivation = gate.transition('meeting-a')
  const secondActivation = gate.transition('meeting-a')
  assert.equal(gate.isCurrent(firstActivation), false)
  assert.equal(gate.isCurrent(secondActivation), true)
})

test('meeting request gate rejects wrong-key and lower-version detail responses in one activation', () => {
  const gate = new MeetingRequestGenerationGate()
  const scope = gate.transition('meeting-a', 4)
  assert.equal(gate.tryCommitVersion(scope, 'meeting-b', 5), false)
  assert.equal(gate.tryCommitVersion(scope, 'meeting-a', 3), false)
  assert.equal(gate.tryCommitVersion(scope, 'meeting-a', 5), true)
  assert.equal(gate.tryCommitVersion(scope, 'meeting-a', 4), false)
  assert.equal(gate.tryCommitVersion(scope, 'meeting-a', Number.NaN), false)

  const replacement = gate.transition('meeting-a', 5)
  assert.equal(gate.tryCommitVersion(scope, 'meeting-a', 6), false)
  assert.equal(gate.tryCommitVersion(replacement, 'meeting-a', 6), true)
})

test('guest request gate rejects responses from a replaced credential', () => {
  const gate = new GuestMeetingRequestGate()
  gate.selectCredential('invite', 'invite-a')
  const preview = gate.begin('preview', 'invite', 'invite-a')
  gate.selectCredential('ticket', 'ticket-b')
  assert.equal(gate.isCurrent(preview), false)
  assert.equal(gate.clearIfCurrent(preview), false)
  assert.equal(gate.matches('ticket', 'ticket-b'), true)
})

test('guest request gate applies only the newest response in one request scope', () => {
  const gate = new GuestMeetingRequestGate()
  gate.selectCredential('ticket', 'ticket-a')
  const olderStatus = gate.begin('status', 'ticket', 'ticket-a')
  const newerStatus = gate.begin('status', 'ticket', 'ticket-a')
  assert.equal(gate.isCurrent(olderStatus), false)
  assert.equal(gate.isCurrent(newerStatus), true)
  assert.equal(gate.begin('status', 'ticket', 'other-ticket'), null)
})

test('guest conditional clear cannot invalidate a newer access ticket', () => {
  const gate = new GuestMeetingRequestGate()
  gate.selectCredential('ticket', 'ticket-a')
  const failedRequest = gate.begin('status', 'ticket', 'ticket-a')
  gate.selectCredential('ticket', 'ticket-b')
  assert.equal(gate.clearIfCurrent(failedRequest), false)
  assert.equal(gate.matches('ticket', 'ticket-b'), true)
})

test('clearing the current guest credential invalidates all outstanding operations', () => {
  const gate = new GuestMeetingRequestGate()
  gate.selectCredential('invite', 'invite-a')
  const preview = gate.begin('preview', 'invite', 'invite-a')
  const redeem = gate.begin('redeem', 'invite', 'invite-a')
  assert.equal(gate.clearCredentialIfCurrent('invite', 'invite-a'), true)
  assert.equal(gate.isCurrent(preview), false)
  assert.equal(gate.isCurrent(redeem), false)
  assert.equal(gate.clearCredentialIfCurrent('invite', 'invite-a'), false)
})

test('guest URL scrubbing removes secrets while preserving route and safe query state', () => {
  assert.equal(
    scrubGuestMeetingCredentialFromUrl('https://erp.hadron.test/meeting/guest?lang=fa&invite=query-leak#invite=secret'),
    '/meeting/guest?lang=fa',
  )
  assert.equal(scrubGuestMeetingCredentialFromUrl('/tenant-a/meeting/guest#invite=secret'), '/tenant-a/meeting/guest')
})

test('meeting request errors never expose raw network text', () => {
  assert.equal(meetingRequestErrorText(new Error('Meeting request failed.'), 'Service unavailable', 'Not allowed'), 'Service unavailable')
  assert.equal(meetingRequestErrorText(new TypeError('Failed to fetch'), 'Service unavailable', 'Not allowed'), 'Service unavailable')
  assert.equal(meetingRequestErrorText(new Error('Forbidden'), 'Service unavailable', 'Not allowed'), 'Not allowed')
  assert.equal(meetingRequestErrorText(new Error('The selected meeting changed.'), 'Service unavailable', 'Not allowed'), 'The selected meeting changed.')
})

test('runtime meeting code guards accept only canonical values', () => {
  assert.equal(isMeetingStatus('LIVE'), true)
  assert.equal(isMeetingStatus('live'), false)
  assert.equal(isMeetingRole('EXTERNAL_GUEST'), true)
  assert.equal(isMeetingRole('OWNER'), false)
  assert.equal(isMeetingAdmissionStatus('WAITING'), true)
  assert.equal(isMeetingAdmissionStatus(null), false)
})

test('status labels use stable translation keys', () => {
  assert.equal(meetingStatusTranslationKey('SCHEDULED'), 'collaborationMeetings.status.SCHEDULED')
  assert.equal(meetingStatusTranslationKey('LIVE'), 'collaborationMeetings.status.LIVE')
  assert.equal(meetingStatusTranslationKey('unexpected'), 'collaborationMeetings.status.UNKNOWN')
})

test('status tones distinguish live, scheduled, cancelled and closed states', () => {
  assert.equal(meetingStatusTone('LIVE'), 'positive')
  assert.equal(meetingStatusTone('SCHEDULED'), 'information')
  assert.equal(meetingStatusTone('CANCELLED'), 'critical')
  assert.equal(meetingStatusTone('ENDED'), 'neutral')
  assert.equal(meetingStatusTone('invalid'), 'neutral')
})

test('role labels never interpolate an untrusted role into a translation path', () => {
  assert.equal(meetingRoleTranslationKey('HOST'), 'collaborationMeetings.roles.HOST')
  assert.equal(meetingRoleTranslationKey('EXTERNAL_GUEST'), 'collaborationMeetings.roles.EXTERNAL_GUEST')
  assert.equal(meetingRoleTranslationKey('../admin'), 'collaborationMeetings.roles.UNKNOWN')
})

test('role tones keep operational roles visually distinct', () => {
  assert.equal(meetingRoleTone('HOST'), 'positive')
  assert.equal(meetingRoleTone('COHOST'), 'information')
  assert.equal(meetingRoleTone('PRESENTER'), 'warning')
  assert.equal(meetingRoleTone('ATTENDEE'), 'neutral')
})

test('admission labels and tones cover the waiting-room lifecycle', () => {
  assert.equal(meetingAdmissionTranslationKey('WAITING'), 'collaborationMeetings.admission.WAITING')
  assert.equal(meetingAdmissionTranslationKey('bad'), 'collaborationMeetings.admission.UNKNOWN')
  assert.equal(meetingAdmissionTone('INVITED'), 'information')
  assert.equal(meetingAdmissionTone('WAITING'), 'warning')
  assert.equal(meetingAdmissionTone('ADMITTED'), 'positive')
  assert.equal(meetingAdmissionTone('DENIED'), 'critical')
  assert.equal(meetingAdmissionTone('REVOKED'), 'critical')
})

test('host, cohost and presenter receive media controls and common policy features', () => {
  for (const role of ['HOST', 'COHOST', 'PRESENTER'] as const) {
    assert.deepEqual(meetingCapabilitiesForRole(role, policy), {
      microphone: true, camera: true, screenShare: true,
      chat: true, fileSharing: true, ephemeralWhiteboard: true, recording: false,
    })
  }
})

test('attendee media capabilities are constrained by attendee policy', () => {
  assert.deepEqual(meetingCapabilitiesForRole('ATTENDEE', policy), {
    microphone: true, camera: false, screenShare: false,
    chat: true, fileSharing: true, ephemeralWhiteboard: true, recording: false,
  })
})

test('external guest media capabilities are constrained independently', () => {
  assert.deepEqual(meetingCapabilitiesForRole('EXTERNAL_GUEST', policy), {
    microphone: false, camera: false, screenShare: false,
    chat: true, fileSharing: false, ephemeralWhiteboard: true, recording: false,
  })
})

test('unknown role fails closed for every meeting capability', () => {
  assert.deepEqual(meetingCapabilitiesForRole('ADMIN', policy), {
    microphone: false, camera: false, screenShare: false,
    chat: false, fileSharing: false, ephemeralWhiteboard: false, recording: false,
  })
})

test('recording remains disabled even for a malformed untrusted policy payload', () => {
  const malicious = { ...policy, recordingEnabled: true } as unknown as MeetingPolicy
  assert.equal(meetingCapabilitiesForRole('HOST', malicious).recording, false)
})

test('invite token validation accepts base64url entropy and rejects unsafe forms', () => {
  assert.equal(isValidMeetingInviteToken(validInvite), true)
  assert.equal(isValidMeetingInviteToken('short'), false)
  assert.equal(isValidMeetingInviteToken(`${validInvite}.payload`), false)
  assert.equal(isValidMeetingInviteToken(`${validInvite}/path`), false)
  assert.equal(isValidMeetingInviteToken(` ${validInvite}`), false)
})

test('invite extraction reads the fragment value from absolute and relative URLs', () => {
  assert.equal(extractMeetingInviteToken(`https://erp.hadron.test/meeting/guest#invite=${validInvite}`), validInvite)
  assert.equal(extractMeetingInviteToken(`/meeting/guest#invite=${validInvite}`), validInvite)
  assert.equal(extractMeetingInviteToken(`#invite=${validInvite}`), validInvite)
})

test('invite extraction never accepts query-string credentials', () => {
  assert.equal(extractMeetingInviteToken(`/meeting/guest?invite=${validInvite}`), null)
  assert.equal(extractMeetingInviteToken(`https://erp.test/meeting/guest?invite=${validInvite}`), null)
})

test('fragment invite remains authoritative when a hostile query token is present', () => {
  const hostile = 'A'.repeat(43)
  assert.equal(
    extractMeetingInviteToken(`/meeting/guest?invite=${hostile}#invite=${validInvite}`),
    validInvite,
  )
})

test('ambiguous duplicate fragment credentials are rejected', () => {
  assert.equal(extractMeetingInviteToken(`#invite=${validInvite}&invite=${'A'.repeat(43)}`), null)
})

test('malformed fragment, unsafe alphabet and encoded whitespace are rejected', () => {
  assert.equal(extractMeetingInviteToken('#not-an-invite'), null)
  assert.equal(extractMeetingInviteToken(`#invite=${validInvite}%20`), null)
  assert.equal(extractMeetingInviteToken(`#invite=${validInvite}%2F`), null)
  assert.equal(extractMeetingInviteToken('http://[invalid'), null)
})

test('canonical route builders generate the approved meeting routes', () => {
  assert.equal(meetingListRoute, '/collaboration/meetings')
  assert.equal(newMeetingRoute, '/collaboration/meetings/new')
  assert.equal(guestMeetingRoute, '/meeting/guest')
  assert.equal(buildMeetingDetailRoute(validId), `/collaboration/meetings/${validId}`)
  const inviteRoute = buildGuestMeetingInviteRoute(validInvite)
  assert.equal(inviteRoute, `/meeting/guest#invite=${validInvite}`)
  assert.equal(inviteRoute.includes('?'), false)
})

test('guest invite URL prefers the configured LAN frontend origin over localhost', () => {
  assert.equal(
    buildGuestMeetingInviteUrl(validInvite, 'http://localhost:5173', 'http://192.168.0.175:5173'),
    `http://192.168.0.175:5173/meeting/guest#invite=${validInvite}`,
  )
})

test('guest invite URL preserves the browser origin when no public origin is configured', () => {
  assert.equal(
    buildGuestMeetingInviteUrl(validInvite, 'https://erp.hadron.example', ''),
    `https://erp.hadron.example/meeting/guest#invite=${validInvite}`,
  )
})

test('unsafe or non-origin invite overrides fail safely back to the browser origin', () => {
  const browserOrigin = 'https://erp.hadron.example'
  for (const configuredOrigin of [
    'http://127.0.0.1:5080/api',
    'javascript:alert(1)',
    'https://user:password@erp.hadron.example',
    'https://erp.hadron.example?redirect=unsafe',
    'https://erp.hadron.example#unsafe',
  ]) {
    assert.equal(
      buildGuestMeetingInviteUrl(validInvite, browserOrigin, configuredOrigin),
      `${browserOrigin}/meeting/guest#invite=${validInvite}`,
    )
  }
})

test('guest invite URL rejects an invalid browser origin when no safe override exists', () => {
  assert.throws(() => buildGuestMeetingInviteUrl(validInvite, 'null'), TypeError)
})

test('meeting list failure is distinct from empty data and keeps stale rows visible', () => {
  assert.equal(shouldShowMeetingListFailure(false, 'Service unavailable', 0), true)
  assert.equal(shouldShowMeetingListFailure(true, 'Service unavailable', 0), false)
  assert.equal(shouldShowMeetingListFailure(false, '', 0), false)
  assert.equal(shouldShowMeetingListFailure(false, 'Refresh failed', 2), false)
})

test('detail route rejects non-Guid identifiers and path injection', () => {
  assert.throws(() => buildMeetingDetailRoute(''), TypeError)
  assert.throws(() => buildMeetingDetailRoute('../../admin'), TypeError)
  assert.throws(() => buildMeetingDetailRoute(`${validId}/report`), TypeError)
})

test('invite route refuses credentials that would produce an unsafe URL', () => {
  assert.throws(() => buildGuestMeetingInviteRoute(''), TypeError)
  assert.throws(() => buildGuestMeetingInviteRoute(`${validInvite}&admin=true`), TypeError)
})

test('Gregorian datetime-local input normalizes Persian and Arabic digits', () => {
  assert.equal(normalizeMeetingLocalDateTimeInput('۲۰۲۶/۰۹/۰۸ ۱۴:۰۵'), '2026-09-08T14:05')
  assert.equal(normalizeMeetingLocalDateTimeInput('٢٠٢٦-٠٩-٠٨T١٤:٠٥'), '2026-09-08T14:05')
})

test('datetime-local normalization rejects impossible and underspecified dates', () => {
  assert.equal(normalizeMeetingLocalDateTimeInput('2026-02-29T12:00'), null)
  assert.equal(normalizeMeetingLocalDateTimeInput('2026-09-08'), null)
  assert.equal(normalizeMeetingLocalDateTimeInput('2026-09-08T24:00'), null)
  assert.equal(normalizeMeetingLocalDateTimeInput('2026-13-01T00:00'), null)
})

test('Tehran wall clock is converted to canonical UTC independent of host timezone', () => {
  assert.equal(meetingLocalDateTimeToUtc('2026-09-08T12:00'), '2026-09-08T08:30:00.000Z')
})

test('UTC instant converts back to the same Tehran wall clock', () => {
  assert.equal(meetingUtcToLocalDateTime('2026-09-08T08:30:00.000Z'), '2026-09-08T12:00')
  assert.equal(meetingUtcToLocalDateTime('2026-09-08T10:30:00+02:00'), '2026-09-08T12:00')
})

test('timezone conversion fails closed for ambiguous input and invalid zones', () => {
  assert.equal(meetingUtcToLocalDateTime('2026-09-08T08:30:00'), null)
  assert.equal(meetingUtcToLocalDateTime('invalid'), null)
  assert.equal(meetingLocalDateTimeToUtc('2026-09-08T12:00', 'Invalid/Zone'), null)
  assert.equal(meetingUtcToLocalDateTime('2026-09-08T08:30:00Z', 'Invalid/Zone'), null)
})

test('scheduled host can maintain and start a meeting but cannot join before it is live', () => {
  assert.deepEqual(meetingLifecycleActions({ status: 'SCHEDULED', isLocked: false }, 'HOST', 'ADMITTED'), {
    canEdit: true, canStart: true, canCancel: true, canJoin: false,
    canLock: true, canUnlock: false, canEnd: false, canInviteGuest: true,
    canManageParticipants: true, canAdmitGuest: true, canChangeRoles: true,
    canEnterWaitingRoom: false,
  })
})

test('live attendee can join but cannot perform host lifecycle actions', () => {
  const actions = meetingLifecycleActions({ status: 'LIVE', isLocked: false }, 'ATTENDEE', 'ADMITTED')
  assert.equal(actions.canJoin, true)
  assert.equal(actions.canStart, false)
  assert.equal(actions.canEnd, false)
  assert.equal(actions.canLock, false)
  assert.equal(actions.canManageParticipants, false)
  assert.equal(actions.canChangeRoles, false)
})

test('an admitted guest may reconnect to a locked live meeting', () => {
  const actions = meetingLifecycleActions({ status: 'LIVE', isLocked: true }, 'EXTERNAL_GUEST', 'ADMITTED')
  assert.equal(actions.canJoin, true)
  assert.equal(actions.canEnterWaitingRoom, false)
})

test('a new guest cannot enter the waiting room while the meeting is locked', () => {
  const locked = meetingLifecycleActions({ status: 'LIVE', isLocked: true }, 'EXTERNAL_GUEST', 'INVITED')
  const open = meetingLifecycleActions({ status: 'LIVE', isLocked: false }, 'EXTERNAL_GUEST', 'INVITED')
  assert.equal(locked.canEnterWaitingRoom, false)
  assert.equal(open.canEnterWaitingRoom, true)
})

test('cohost can manage a live meeting but only the host changes roles', () => {
  const actions = meetingLifecycleActions({ status: 'LIVE', isLocked: false }, 'COHOST', 'ADMITTED')
  assert.equal(actions.canJoin, true)
  assert.equal(actions.canEnd, true)
  assert.equal(actions.canAdmitGuest, true)
  assert.equal(actions.canChangeRoles, false)
})

test('scheduled cohost can manage lifecycle but cannot edit host-owned meeting details', () => {
  const actions = meetingLifecycleActions({ status: 'SCHEDULED', isLocked: false }, 'COHOST', 'ADMITTED')
  assert.equal(actions.canEdit, false)
  assert.equal(actions.canStart, true)
  assert.equal(actions.canCancel, true)
  assert.equal(actions.canManageParticipants, true)
})

test('locked manager can unlock but cannot issue another guest invite or admission', () => {
  const actions = meetingLifecycleActions({ status: 'LIVE', isLocked: true }, 'HOST', 'ADMITTED')
  assert.equal(actions.canLock, false)
  assert.equal(actions.canUnlock, true)
  assert.equal(actions.canInviteGuest, false)
  assert.equal(actions.canAdmitGuest, false)
})

test('terminal meetings expose no lifecycle mutation or join actions', () => {
  for (const status of ['ENDED', 'CANCELLED'] as const) {
    const actions = meetingLifecycleActions({ status, isLocked: true }, 'HOST', 'ADMITTED')
    assert.equal(Object.values(actions).some(Boolean), false)
  }
})

test('unknown roles and admissions fail closed even for a live meeting', () => {
  const actions = meetingLifecycleActions({ status: 'LIVE', isLocked: false }, 'ADMIN', 'ADMITTED')
  assert.equal(Object.values(actions).some(Boolean), false)
  assert.equal(
    meetingLifecycleActions({ status: 'LIVE', isLocked: false }, 'ATTENDEE', 'UNKNOWN').canJoin,
    false,
  )
})

test('guest access ticket expiry is deterministic and invalid timestamps fail closed', () => {
  const now = '2026-09-08T10:00:00.000Z'
  assert.equal(isGuestAccessTicketExpired('2026-09-08T10:00:01.000Z', now), false)
  assert.equal(isGuestAccessTicketExpired('2026-09-08T10:00:00.000Z', now), true)
  assert.equal(isGuestAccessTicketExpired('2026-09-08T09:59:59.000Z', now), true)
  assert.equal(isGuestAccessTicketExpired('not-a-date', now), true)
  assert.equal(isGuestAccessTicketExpired('2026-02-30T11:00:00.000Z', now), true)
  assert.equal(isGuestAccessTicketExpired('2026-09-08T11:00:00.000Z', 'local-time-without-zone'), true)
  assert.equal(isGuestAccessTicketExpired('2026-09-08T11:00:00.000Z', new Date('invalid')), true)
  assert.equal(isGuestAccessTicketExpired('2026-09-08T11:00:00.000Z', Number.NaN), true)
})

test('guest status polling uses a deterministic ten-second jittered cadence', () => {
  assert.equal(meetingPollingDelayMs(guestMeetingStatusPollingProfile, 0, 0), 8_500)
  assert.equal(meetingPollingDelayMs(guestMeetingStatusPollingProfile, 0, 0.5), 10_000)
  assert.equal(meetingPollingDelayMs(guestMeetingStatusPollingProfile, 0, 1), 11_500)
})

test('active meeting detail polling uses a deterministic twelve-second cadence', () => {
  assert.equal(meetingPollingDelayMs(activeMeetingDetailPollingProfile, 0, 0), 10_200)
  assert.equal(meetingPollingDelayMs(activeMeetingDetailPollingProfile, 0, 0.5), 12_000)
  assert.equal(meetingPollingDelayMs(activeMeetingDetailPollingProfile, 0, 1), 13_800)
})

test('meeting polling backs off after failures and remains bounded', () => {
  assert.equal(meetingPollingDelayMs(guestMeetingStatusPollingProfile, 1, 0.5), 20_000)
  assert.equal(meetingPollingDelayMs(guestMeetingStatusPollingProfile, 2, 0.5), 40_000)
  assert.equal(meetingPollingDelayMs(guestMeetingStatusPollingProfile, 3, 0.5), 60_000)
  assert.equal(meetingPollingDelayMs(guestMeetingStatusPollingProfile, 99, 1), 60_000)
  assert.equal(meetingPollingDelayMs(guestMeetingStatusPollingProfile, -1, -1), 8_500)
  assert.equal(meetingPollingDelayMs(guestMeetingStatusPollingProfile, 0, 2), 11_500)
})

test('guest ticket survives transient, throttled and unknown polling failures', () => {
  assert.equal(meetingRequestHttpStatus({ status: 429 }), 429)
  assert.equal(meetingRequestHttpStatus({ status: 503 }), 503)
  assert.equal(meetingRequestHttpStatus(new Error('network unavailable')), null)
  assert.equal(meetingRequestHttpStatus({ status: '401' }), null)
  assert.equal(shouldDiscardGuestMeetingTicket({ status: 429 }), false)
  assert.equal(shouldDiscardGuestMeetingTicket({ status: 503 }), false)
  assert.equal(shouldDiscardGuestMeetingTicket(new Error('network unavailable')), false)
})

test('guest ticket is discarded only after authoritative credential rejection', () => {
  assert.equal(shouldDiscardGuestMeetingTicket({ status: 401 }), true)
  assert.equal(shouldDiscardGuestMeetingTicket({ status: 410 }), true)
  assert.equal(shouldDiscardGuestMeetingTicket({ status: 403 }), false)
  assert.equal(shouldDiscardGuestMeetingTicket({ status: 404 }), false)
})
