import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import {
  MAX_EPHEMERAL_CHAT_ENTRIES,
  MAX_STAGE_PLACEHOLDER_TILES,
  MAX_STAGE_VISUAL_TILES,
  MAX_VISIBLE_REMOTE_CAMERAS,
  MAX_WHITEBOARD_STROKES,
  WHITEBOARD_MIN_UPDATE_INTERVAL_MS,
  appendBoundedUnique,
  canonicalMeetingChatTimestamp,
  canEmitWhiteboardUpdate,
  connectionQualityPresentation,
  createMeetingParticipantIndex,
  formatMeetingChatTime,
  participantMatchesMeetingSearch,
  reportMeetingAttendanceWithRetry,
  resolveMeetingParticipant,
  resolveMeetingShortcut,
  selectRemoteCameraTrackSids,
  selectStageMediaTrackSids,
  selectStageParticipantPlaceholders,
  MeetingMediaOperationGate,
  type CameraSubscriptionCandidate,
} from './meetingRoomScaleSupport.ts'
import type { MeetingParticipant } from './collaborationMeetingSupport.ts'

const meetingRoomSource = readFileSync(new URL('./CollaborationMeetingRoom.tsx', import.meta.url), 'utf8')

const participant = (id: string, userId: string | null, displayName: string): MeetingParticipant => ({
  id,
  userId,
  displayName,
  role: 'ATTENDEE',
  admissionStatus: 'ADMITTED',
  createdAtUtc: '2026-09-08T00:00:00Z',
  version: 1,
})

test('camera selection is bounded, speaker-first, role-aware, and deterministic', () => {
  const roles = ['ATTENDEE', 'PRESENTER', 'COHOST', 'HOST', 'EXTERNAL_GUEST'] as const
  const candidates: CameraSubscriptionCandidate[] = Array.from({ length: 24 }, (_, index) => ({
    trackSid: `track-${String(index).padStart(2, '0')}`,
    participantIdentity: `erp:${String(index).padStart(2, '0')}`,
    role: roles[index % roles.length],
    muted: false,
  }))
  const selected = selectRemoteCameraTrackSids(candidates.slice().reverse(), ['erp:19', 'erp:04'])
  assert.equal(selected.length, MAX_VISIBLE_REMOTE_CAMERAS)
  assert.deepEqual(selected.slice(0, 2), ['track-19', 'track-04'])
  assert.deepEqual(selected, selectRemoteCameraTrackSids(candidates.slice().reverse(), ['erp:19', 'erp:04']))
  assert.ok(selected.indexOf('track-03') < selected.indexOf('track-02'))
})

test('camera selection excludes muted and duplicate publications without wasting capacity', () => {
  const candidates: CameraSubscriptionCandidate[] = [
    { trackSid: 'muted', participantIdentity: 'speaker', role: 'HOST', muted: true },
    { trackSid: 'camera', participantIdentity: 'speaker', role: 'ATTENDEE', muted: false },
    { trackSid: 'camera', participantIdentity: 'duplicate', role: 'HOST', muted: false },
  ]
  assert.deepEqual(selectRemoteCameraTrackSids(candidates, ['speaker'], 3), ['camera'])
  assert.deepEqual(selectRemoteCameraTrackSids(candidates, [], -1), [])
})

test('camera selection keeps a sticky same-role window while admitting a new active speaker', () => {
  const candidates: CameraSubscriptionCandidate[] = ['a', 'b', 'c', 'd'].map(identity => ({
    trackSid: `camera-${identity}`,
    participantIdentity: identity,
    role: 'ATTENDEE',
    muted: false,
  }))
  const first = selectRemoteCameraTrackSids(candidates, [], 2)
  assert.deepEqual(first, ['camera-a', 'camera-b'])

  const alreadyVisibleSpeaker = selectRemoteCameraTrackSids(candidates, ['b'], 2, first)
  assert.deepEqual(new Set(alreadyVisibleSpeaker), new Set(first))

  const newSpeaker = selectRemoteCameraTrackSids(candidates, ['d'], 2, first)
  assert.deepEqual(newSpeaker, ['camera-d', 'camera-a'])
  assert.equal(newSpeaker.filter(trackSid => !first.includes(trackSid)).length, 1)
})

test('one stage budget covers local tiles, screen shares, and remote cameras', () => {
  const candidates = [
    ...Array.from({ length: 4 }, (_, index) => ({
      trackSid: `screen-${index}`,
      participantIdentity: `screen-user-${index}`,
      role: (index === 3 ? 'HOST' : 'ATTENDEE') as 'HOST' | 'ATTENDEE',
      muted: false,
      mediaKind: 'screen' as const,
    })),
    ...Array.from({ length: 20 }, (_, index) => ({
      trackSid: `camera-${index}`,
      participantIdentity: `camera-user-${index}`,
      role: 'ATTENDEE' as const,
      muted: false,
      mediaKind: 'camera' as const,
    })),
  ]

  const selected = selectStageMediaTrackSids(candidates, ['camera-user-19'], 2)
  assert.equal(selected.length + 2, MAX_STAGE_VISUAL_TILES)
  assert.equal(selected[0], 'screen-3')
  assert.ok(selected.includes('camera-19'))
  assert.deepEqual(selectStageMediaTrackSids(candidates.slice().reverse(), ['camera-user-19'], 2), selected)
  assert.deepEqual(selectStageMediaTrackSids(candidates, [], MAX_STAGE_VISUAL_TILES), [])
})

test('large-room stage placeholders are bounded with local and operational roles first', () => {
  const roles = ['ATTENDEE', 'EXTERNAL_GUEST', 'PRESENTER', 'COHOST', 'HOST'] as const
  const candidates = Array.from({ length: 150 }, (_, index) => ({
    key: `participant-${String(index).padStart(3, '0')}`,
    role: roles[index % roles.length],
    isLocal: index === 149,
    cameraVisible: false,
  }))

  const presentation = selectStageParticipantPlaceholders(candidates.slice().reverse())
  assert.equal(presentation.visibleParticipants.length + 1, MAX_STAGE_PLACEHOLDER_TILES)
  assert.equal(presentation.visibleParticipants[0]?.key, 'participant-149')
  assert.equal(presentation.visibleParticipants[1]?.role, 'HOST')
  assert.equal(presentation.summarizedParticipantCount, 135)
  assert.deepEqual(
    presentation,
    selectStageParticipantPlaceholders(candidates.slice().reverse()),
  )
})

test('stage placeholder summary appears only on overflow and camera tiles are excluded', () => {
  const candidates = Array.from({ length: MAX_STAGE_PLACEHOLDER_TILES + 1 }, (_, index) => ({
    key: `participant-${index}`,
    role: 'ATTENDEE' as const,
    isLocal: false,
    cameraVisible: index === 0,
  }))

  const fitted = selectStageParticipantPlaceholders(candidates)
  assert.equal(fitted.visibleParticipants.length, MAX_STAGE_PLACEHOLDER_TILES)
  assert.equal(fitted.summarizedParticipantCount, 0)
  assert.equal(fitted.visibleParticipants.some(participant => participant.cameraVisible), false)

  const overflow = selectStageParticipantPlaceholders([...candidates, {
    key: 'participant-overflow', role: 'ATTENDEE', isLocal: false, cameraVisible: false,
  }])
  assert.equal(overflow.visibleParticipants.length, MAX_STAGE_PLACEHOLDER_TILES - 1)
  assert.equal(overflow.summarizedParticipantCount, 2)
})

test('stage placeholder priority is local, host, cohost, presenter, attendee, then guest', () => {
  const presentation = selectStageParticipantPlaceholders([
    { key: 'guest', role: 'EXTERNAL_GUEST', isLocal: false, cameraVisible: false },
    { key: 'attendee', role: 'ATTENDEE', isLocal: false, cameraVisible: false },
    { key: 'presenter', role: 'PRESENTER', isLocal: false, cameraVisible: false },
    { key: 'cohost', role: 'COHOST', isLocal: false, cameraVisible: false },
    { key: 'host', role: 'HOST', isLocal: false, cameraVisible: false },
    { key: 'local', role: 'EXTERNAL_GUEST', isLocal: true, cameraVisible: false },
  ], 5)

  assert.deepEqual(presentation.visibleParticipants.map(participant => participant.key), [
    'local', 'host', 'cohost', 'presenter',
  ])
  assert.equal(presentation.summarizedParticipantCount, 2)
})

test('rendered media and placeholders share the same bounded stage budget', () => {
  const candidates = Array.from({ length: 30 }, (_, index) => ({
    key: `participant-${index}`,
    role: 'ATTENDEE' as const,
    isLocal: false,
    cameraVisible: false,
  }))
  const occupied = [
    { participantKey: 'participant-0', mediaKind: 'screen' as const },
    ...Array.from({ length: 5 }, (_, index) => ({
      participantKey: `participant-${index + 1}`,
      mediaKind: 'camera' as const,
    })),
  ]
  const presentation = selectStageParticipantPlaceholders(candidates, MAX_STAGE_VISUAL_TILES, occupied)
  const summaryTiles = presentation.summarizedParticipantCount > 0 ? 1 : 0
  assert.ok(occupied.length + presentation.visibleParticipants.length + summaryTiles <= MAX_STAGE_VISUAL_TILES)
  assert.equal(presentation.visibleParticipants.some(item => occupied.some(tile => tile.participantKey === item.key)), false)
  assert.equal(presentation.summarizedParticipantCount, 15)

  const noCapacity = selectStageParticipantPlaceholders(candidates, MAX_STAGE_VISUAL_TILES, Array.from({ length: MAX_STAGE_VISUAL_TILES }, (_, index) => ({
    participantKey: `media-${index}`,
    mediaKind: 'camera' as const,
  })))
  assert.deepEqual(noCapacity, { visibleParticipants: [], summarizedParticipantCount: 0 })
})

test('participant index resolves metadata, ERP/guest identity, names, and malformed metadata safely', () => {
  const guest = participant('11111111-1111-1111-1111-111111111111', null, 'Guest One')
  const employee = participant('22222222-2222-2222-2222-222222222222', '33333333-3333-3333-3333-333333333333', 'Employee One')
  const index = createMeetingParticipantIndex([guest, employee])
  assert.equal(resolveMeetingParticipant(index, { identity: 'provider', displayName: 'x', metadata: JSON.stringify({ participantId: guest.id }) }), guest)
  assert.equal(resolveMeetingParticipant(index, { identity: `participant:${employee.id}`, displayName: 'x' }), employee)
  assert.equal(resolveMeetingParticipant(index, { identity: 'erp:any', displayName: 'x', metadata: JSON.stringify({ erpUserId: employee.userId }) }), employee)
  assert.equal(resolveMeetingParticipant(index, { identity: `guest:${guest.id}`, displayName: 'x' }), guest)
  assert.equal(resolveMeetingParticipant(index, { identity: `erp:${employee.userId}`, displayName: 'x' }), employee)
  assert.equal(resolveMeetingParticipant(index, { identity: 'unknown', displayName: ' Employee One ', metadata: '{bad' }), employee)
})

test('bounded unique history caps chat and whiteboard while ignoring replayed ids', () => {
  const chat = Array.from({ length: MAX_EPHEMERAL_CHAT_ENTRIES + 20 }, (_, index) => ({ id: `chat-${index}` }))
  const boundedChat = appendBoundedUnique([], chat, MAX_EPHEMERAL_CHAT_ENTRIES)
  assert.equal(boundedChat.length, MAX_EPHEMERAL_CHAT_ENTRIES)
  assert.equal(boundedChat[0].id, 'chat-20')
  assert.equal(appendBoundedUnique(boundedChat, [{ id: 'chat-519' }, { id: 'chat-520' }], MAX_EPHEMERAL_CHAT_ENTRIES).at(-1)?.id, 'chat-520')

  const strokes = Array.from({ length: MAX_WHITEBOARD_STROKES + 1 }, (_, index) => ({ id: `stroke-${index}` }))
  const boundedStrokes = appendBoundedUnique([], strokes, MAX_WHITEBOARD_STROKES)
  assert.equal(boundedStrokes.length, MAX_WHITEBOARD_STROKES)
  assert.equal(boundedStrokes[0].id, 'stroke-1')
})

test('hostile chat timestamps are canonicalized or replaced with a deterministic safe fallback', () => {
  const fallback = '2026-09-08T12:30:00.000Z'
  assert.equal(canonicalMeetingChatTimestamp('2026-09-08T16:00:00+03:30', fallback), '2026-09-08T12:30:00.000Z')
  assert.equal(canonicalMeetingChatTimestamp('not-a-date', fallback), fallback)
  assert.equal(canonicalMeetingChatTimestamp(123, fallback), fallback)
  assert.equal(canonicalMeetingChatTimestamp(undefined, 'also-invalid'), '1970-01-01T00:00:00.000Z')
  assert.doesNotThrow(() => new Intl.DateTimeFormat('en').format(new Date(canonicalMeetingChatTimestamp('bad', fallback))))
})

test('chat time uses the active ERP locale and Tehran timezone rather than the host locale', () => {
  const timestamp = '2026-09-08T12:30:00.000Z'
  assert.equal(formatMeetingChatTime(timestamp, 'en'), '16:00')
  assert.equal(formatMeetingChatTime(timestamp, 'fa-IR'), '۱۶:۰۰')
  assert.equal(formatMeetingChatTime('invalid', 'fa-IR'), '—')
})

test('media operation gate rejects duplicates and invalidates late lifecycle completions', () => {
  const gate = new MeetingMediaOperationGate()
  const firstGeneration = gate.startLifecycle()
  assert.equal(gate.tryStart('MICROPHONE', firstGeneration), true)
  assert.equal(gate.tryStart('MICROPHONE', firstGeneration), false)
  assert.equal(gate.tryStart('CAMERA', firstGeneration), true)
  gate.finish('MICROPHONE', firstGeneration)
  assert.equal(gate.tryStart('MICROPHONE', firstGeneration), true)

  gate.stopLifecycle(firstGeneration)
  assert.equal(gate.isCurrent(firstGeneration), false)
  assert.equal(gate.tryStart('SCREEN_SHARE', firstGeneration), false)
  const replacementGeneration = gate.startLifecycle()
  assert.equal(gate.tryStart('SCREEN_SHARE', replacementGeneration), true)
  gate.finish('SCREEN_SHARE', firstGeneration)
  assert.equal(gate.tryStart('SCREEN_SHARE', replacementGeneration), false)
})

test('attendance reporting retries bounded transient failures and preserves backoff order', async () => {
  let attempts = 0
  const delays: number[] = []
  const reported = await reportMeetingAttendanceWithRetry(async () => {
    attempts += 1
    if (attempts < 3) throw Object.assign(new Error('temporary'), { status: 503 })
  }, {
    baseDelayMs: 25,
    wait: async delayMs => { delays.push(delayMs) },
  })

  assert.equal(reported, true)
  assert.equal(attempts, 3)
  assert.deepEqual(delays, [25, 50])
})

test('attendance reporting does not retry permanent rejection and reports bounded exhaustion', async () => {
  let rejectedAttempts = 0
  const rejected = await reportMeetingAttendanceWithRetry(async () => {
    rejectedAttempts += 1
    throw Object.assign(new Error('forbidden'), { status: 403 })
  }, { wait: async () => undefined })
  assert.equal(rejected, false)
  assert.equal(rejectedAttempts, 1)

  let transientAttempts = 0
  const exhausted = await reportMeetingAttendanceWithRetry(async () => {
    transientAttempts += 1
    throw new TypeError('network unavailable')
  }, { maxAttempts: 2, wait: async () => undefined })
  assert.equal(exhausted, false)
  assert.equal(transientAttempts, 2)
  assert.equal(await reportMeetingAttendanceWithRetry(undefined), true)
})

test('whiteboard throttle admits no more than 20 updates per second', () => {
  assert.equal(canEmitWhiteboardUpdate(Number.NEGATIVE_INFINITY, 0), true)
  assert.equal(canEmitWhiteboardUpdate(100, 100 + WHITEBOARD_MIN_UPDATE_INTERVAL_MS - 1), false)
  assert.equal(canEmitWhiteboardUpdate(100, 100 + WHITEBOARD_MIN_UPDATE_INTERVAL_MS), true)
  assert.equal(canEmitWhiteboardUpdate(100, 99), false)
  assert.equal(canEmitWhiteboardUpdate(100, Number.NaN), false)
})

test('connection quality uses semantic text presentations without invented measurements', () => {
  assert.deepEqual(connectionQualityPresentation('excellent'), {
    tone: 'positive',
    labelKey: 'collaborationMeetings.quality.excellent',
    guidanceKey: 'collaborationMeetings.qualityGuidance.excellent',
  })
  assert.equal(connectionQualityPresentation('poor').tone, 'warning')
  assert.equal(connectionQualityPresentation('lost').tone, 'critical')
  assert.equal(connectionQualityPresentation('unknown').tone, 'information')
})

test('meeting shortcuts require exact Ctrl combinations and ignore editable/repeated events', () => {
  const base = { ctrlKey: true, shiftKey: false, altKey: false, metaKey: false, repeat: false, editableTarget: false }
  assert.equal(resolveMeetingShortcut({ ...base, key: 'd' }), 'MICROPHONE')
  assert.equal(resolveMeetingShortcut({ ...base, key: 'E' }), 'CAMERA')
  assert.equal(resolveMeetingShortcut({ ...base, key: 's', shiftKey: true }), 'SCREEN_SHARE')
  assert.equal(resolveMeetingShortcut({ ...base, key: 's' }), null)
  assert.equal(resolveMeetingShortcut({ ...base, key: 'd', editableTarget: true }), null)
  assert.equal(resolveMeetingShortcut({ ...base, key: 'd', repeat: true }), null)
  assert.equal(resolveMeetingShortcut({ ...base, key: 'd', metaKey: true }), null)
})

test('participant search matches display name or localized role and tolerates spacing/case', () => {
  assert.equal(participantMatchesMeetingSearch('Shayan Test', 'Host', ' shayan '), true)
  assert.equal(participantMatchesMeetingSearch('Shayan Test', 'Host', 'HOST'), true)
  assert.equal(participantMatchesMeetingSearch('Shayan Test', 'Host', 'guest'), false)
  assert.equal(participantMatchesMeetingSearch('مهمان هادرون', 'مهمان خارجی', 'خارجی'), true)
})

test('roster polling refreshes the People panel without restarting the LiveKit room lifecycle', () => {
  assert.match(meetingRoomSource, /const rosterRef = useRef<readonly MeetingRoomRosterParticipant\[\]>/)
  assert.match(meetingRoomSource, /const refreshParticipants = useCallback\([\s\S]*?\n\s{2}\}, \[\]\)\n/)

  const lifecycle = meetingRoomSource.match(/useEffect\(\(\) => \{\s+let mounted = true[\s\S]*?\n\s{2}\}, \[([^\]]*)\]\)/)
  assert.ok(lifecycle, 'the LiveKit room lifecycle effect must remain explicit')
  assert.doesNotMatch(lifecycle[1], /meetingParticipants|participantIndex|join\.meeting\.participants/)
})
