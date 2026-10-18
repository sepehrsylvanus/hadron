import type { MeetingRole, MeetingRoomRosterParticipant, MeetingTone } from './collaborationMeetingSupport'

export const MAX_STAGE_VISUAL_TILES = 16
export const MAX_VISIBLE_REMOTE_CAMERAS = MAX_STAGE_VISUAL_TILES
export const MAX_STAGE_PLACEHOLDER_TILES = MAX_STAGE_VISUAL_TILES
export const MAX_EPHEMERAL_CHAT_ENTRIES = 500
export const MAX_WHITEBOARD_STROKES = 2_000
export const WHITEBOARD_UPDATES_PER_SECOND = 20
export const WHITEBOARD_MIN_UPDATE_INTERVAL_MS = 1_000 / WHITEBOARD_UPDATES_PER_SECOND

export type CameraSubscriptionCandidate = Readonly<{
  trackSid: string
  participantIdentity: string
  role: MeetingRole
  muted: boolean
}>

export type StageMediaSubscriptionCandidate = CameraSubscriptionCandidate & Readonly<{
  mediaKind: 'screen' | 'camera'
}>

export type StageVisualTile = Readonly<{
  participantKey: string
  mediaKind: 'screen' | 'camera'
}>

export type StageParticipantCandidate = Readonly<{
  key: string
  role: MeetingRole
  isLocal: boolean
  cameraVisible: boolean
}>

export type RemoteParticipantDescriptor = Readonly<{
  identity: string
  displayName: string
  metadata?: string
}>

export type MeetingParticipantIndex = Readonly<{
  byParticipantId: ReadonlyMap<string, MeetingRoomRosterParticipant>
  byUserId: ReadonlyMap<string, MeetingRoomRosterParticipant>
  byDisplayName: ReadonlyMap<string, MeetingRoomRosterParticipant>
}>

export type PublicConnectionQuality = 'excellent' | 'good' | 'poor' | 'lost' | 'unknown'

export type MeetingShortcut = 'MICROPHONE' | 'CAMERA' | 'SCREEN_SHARE'

export type MeetingMediaOperation = 'MICROPHONE' | 'CAMERA' | 'SCREEN_SHARE'

export type MeetingShortcutInput = Readonly<{
  key: string
  ctrlKey: boolean
  shiftKey: boolean
  altKey: boolean
  metaKey: boolean
  repeat: boolean
  editableTarget: boolean
}>

const rolePriority: Record<MeetingRole, number> = {
  HOST: 0,
  COHOST: 1,
  PRESENTER: 2,
  ATTENDEE: 3,
  EXTERNAL_GUEST: 4,
}

/**
 * Keeps asynchronous media work scoped to one concrete Room lifecycle and
 * serializes each source independently. A completion from an older lifecycle
 * can therefore never release a lock or publish state into the replacement room.
 */
export class MeetingMediaOperationGate {
  private generation = 0
  private readonly active = new Set<MeetingMediaOperation>()

  startLifecycle() {
    this.active.clear()
    this.generation += 1
    return this.generation
  }

  stopLifecycle(generation: number) {
    if (generation !== this.generation) return
    this.active.clear()
    this.generation += 1
  }

  currentGeneration() {
    return this.generation
  }

  isCurrent(generation: number) {
    return generation === this.generation
  }

  tryStart(operation: MeetingMediaOperation, generation: number) {
    if (!this.isCurrent(generation) || this.active.has(operation)) return false
    this.active.add(operation)
    return true
  }

  finish(operation: MeetingMediaOperation, generation: number) {
    if (this.isCurrent(generation)) this.active.delete(operation)
  }
}

export type MeetingAttendanceRetryOptions = Readonly<{
  maxAttempts?: number
  baseDelayMs?: number
  wait?: (delayMs: number) => Promise<void>
}>

function attendanceRequestStatus(error: unknown) {
  if (!error || typeof error !== 'object' || !('status' in error)) return null
  const status = (error as { status?: unknown }).status
  return typeof status === 'number' && Number.isInteger(status) ? status : null
}

function isRetryableAttendanceFailure(error: unknown) {
  const status = attendanceRequestStatus(error)
  return status === null || status === 408 || status === 429 || status >= 500
}

/**
 * Attendance writes are idempotent on the server. This bounded retry absorbs a
 * short network interruption without creating duplicate attendance intervals.
 */
export async function reportMeetingAttendanceWithRetry(
  callback: (() => void | Promise<void>) | undefined,
  options: MeetingAttendanceRetryOptions = {},
) {
  if (!callback) return true
  const maxAttempts = Math.min(3, Math.max(1, Math.trunc(options.maxAttempts ?? 3)))
  const baseDelayMs = Math.max(0, Math.trunc(options.baseDelayMs ?? 250))
  const wait = options.wait ?? (delayMs => new Promise<void>(resolve => window.setTimeout(resolve, delayMs)))
  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    try {
      await callback()
      return true
    } catch (error) {
      if (attempt === maxAttempts || !isRetryableAttendanceFailure(error)) return false
      await wait(baseDelayMs * (2 ** (attempt - 1)))
    }
  }
  return false
}

function normalizeGuid(value: unknown) {
  return typeof value === 'string' ? value.replaceAll('-', '').trim().toLowerCase() : ''
}

function normalizeSearchValue(value: string) {
  return value.normalize('NFKC').trim().toLocaleLowerCase()
}

/**
 * Chooses the bounded remote-camera window deterministically. Active speakers are
 * ordered first, then operational roles, then stable participant/track identity.
 */
export function selectRemoteCameraTrackSids(
  candidates: readonly CameraSubscriptionCandidate[],
  activeSpeakerIdentities: readonly string[],
  limit = MAX_VISIBLE_REMOTE_CAMERAS,
  previouslySelectedTrackSids: readonly string[] = [],
) {
  const safeLimit = Math.max(0, Math.floor(limit))
  const activeRank = new Map(activeSpeakerIdentities.map((identity, index) => [identity, index]))
  const previousRank = new Map(previouslySelectedTrackSids.map((trackSid, index) => [trackSid, index]))
  const unique = new Map<string, CameraSubscriptionCandidate>()
  candidates.forEach(candidate => {
    if (!candidate.muted && candidate.trackSid && !unique.has(candidate.trackSid)) unique.set(candidate.trackSid, candidate)
  })

  return Array.from(unique.values())
    .sort((left, right) => {
      const leftActive = activeRank.get(left.participantIdentity)
      const rightActive = activeRank.get(right.participantIdentity)
      if (leftActive !== undefined || rightActive !== undefined) {
        if (leftActive === undefined) return 1
        if (rightActive === undefined) return -1
        if (leftActive !== rightActive) return leftActive - rightActive
      }
      const roleDifference = rolePriority[left.role] - rolePriority[right.role]
      if (roleDifference !== 0) return roleDifference
      const leftPrevious = previousRank.get(left.trackSid)
      const rightPrevious = previousRank.get(right.trackSid)
      if (leftPrevious !== undefined || rightPrevious !== undefined) {
        if (leftPrevious === undefined) return 1
        if (rightPrevious === undefined) return -1
        if (leftPrevious !== rightPrevious) return leftPrevious - rightPrevious
      }
      const identityDifference = left.participantIdentity.localeCompare(right.participantIdentity)
      return identityDifference !== 0 ? identityDifference : left.trackSid.localeCompare(right.trackSid)
    })
    .slice(0, safeLimit)
    .map(candidate => candidate.trackSid)
}

/**
 * Allocates one visual-stage budget across local media, remote screen shares and
 * remote cameras. Screen shares are operationally primary; cameras use a sticky
 * previous selection inside the same role so frequent speaker events do not
 * cause unnecessary unsubscribe/subscribe churn.
 */
export function selectStageMediaTrackSids(
  candidates: readonly StageMediaSubscriptionCandidate[],
  activeSpeakerIdentities: readonly string[],
  localVisualTileCount: number,
  limit = MAX_STAGE_VISUAL_TILES,
  previouslySelectedTrackSids: readonly string[] = [],
) {
  const safeLimit = Math.max(0, Math.floor(limit))
  const safeLocalCount = Math.min(safeLimit, Math.max(0, Math.floor(localVisualTileCount)))
  const remoteCapacity = safeLimit - safeLocalCount
  if (remoteCapacity === 0) return []

  const previousRank = new Map(previouslySelectedTrackSids.map((trackSid, index) => [trackSid, index]))
  const uniqueScreens = new Map<string, StageMediaSubscriptionCandidate>()
  const cameras: CameraSubscriptionCandidate[] = []
  candidates.forEach(candidate => {
    if (candidate.muted || !candidate.trackSid) return
    if (candidate.mediaKind === 'screen') {
      if (!uniqueScreens.has(candidate.trackSid)) uniqueScreens.set(candidate.trackSid, candidate)
      return
    }
    cameras.push(candidate)
  })

  const selectedScreens = Array.from(uniqueScreens.values())
    .sort((left, right) => {
      const roleDifference = rolePriority[left.role] - rolePriority[right.role]
      if (roleDifference !== 0) return roleDifference
      const leftPrevious = previousRank.get(left.trackSid)
      const rightPrevious = previousRank.get(right.trackSid)
      if (leftPrevious !== undefined || rightPrevious !== undefined) {
        if (leftPrevious === undefined) return 1
        if (rightPrevious === undefined) return -1
        if (leftPrevious !== rightPrevious) return leftPrevious - rightPrevious
      }
      const identityDifference = left.participantIdentity.localeCompare(right.participantIdentity)
      return identityDifference !== 0 ? identityDifference : left.trackSid.localeCompare(right.trackSid)
    })
    .slice(0, remoteCapacity)
    .map(candidate => candidate.trackSid)

  const cameraCapacity = remoteCapacity - selectedScreens.length
  const selectedCameras = selectRemoteCameraTrackSids(
    cameras,
    activeSpeakerIdentities,
    cameraCapacity,
    previouslySelectedTrackSids.filter(trackSid => !selectedScreens.includes(trackSid)),
  )
  return [...selectedScreens, ...selectedCameras]
}

/**
 * Keeps camera-off placeholders bounded for large rooms. When the bounded stage
 * would overflow, one slot is reserved for a summary tile while local and
 * operational roles remain visible first. The People panel can still use the
 * original, unbounded participant collection.
 */
export function selectStageParticipantPlaceholders<T extends StageParticipantCandidate>(
  candidates: readonly T[],
  limit = MAX_STAGE_PLACEHOLDER_TILES,
  occupiedVisualTiles: readonly StageVisualTile[] = [],
): Readonly<{
  visibleParticipants: readonly T[]
  summarizedParticipantCount: number
}> {
  const stageLimit = Math.max(0, Math.floor(limit))
  const safeLimit = Math.max(0, stageLimit - occupiedVisualTiles.length)
  if (safeLimit === 0) return { visibleParticipants: [], summarizedParticipantCount: 0 }

  const occupiedParticipantKeys = new Set(occupiedVisualTiles.map(tile => tile.participantKey))

  const unique = new Map<string, T>()
  candidates.forEach(candidate => {
    if (!candidate.cameraVisible && !occupiedParticipantKeys.has(candidate.key) && candidate.key && !unique.has(candidate.key)) unique.set(candidate.key, candidate)
  })
  const ordered = Array.from(unique.values()).sort((left, right) => {
    if (left.isLocal !== right.isLocal) return left.isLocal ? -1 : 1
    const roleDifference = rolePriority[left.role] - rolePriority[right.role]
    return roleDifference !== 0 ? roleDifference : left.key.localeCompare(right.key)
  })

  if (ordered.length <= safeLimit) {
    return { visibleParticipants: ordered, summarizedParticipantCount: 0 }
  }

  const visibleParticipants = ordered.slice(0, Math.max(0, safeLimit - 1))
  return {
    visibleParticipants,
    summarizedParticipantCount: ordered.length - visibleParticipants.length,
  }
}

export function createMeetingParticipantIndex(participants: readonly MeetingRoomRosterParticipant[]): MeetingParticipantIndex {
  const byParticipantId = new Map<string, MeetingRoomRosterParticipant>()
  const byUserId = new Map<string, MeetingRoomRosterParticipant>()
  const byDisplayName = new Map<string, MeetingRoomRosterParticipant>()
  participants.forEach(participant => {
    const participantId = normalizeGuid(participant.id)
    const userId = normalizeGuid(participant.userId)
    const displayName = normalizeSearchValue(participant.displayName)
    if (participantId && !byParticipantId.has(participantId)) byParticipantId.set(participantId, participant)
    if (userId && !byUserId.has(userId)) byUserId.set(userId, participant)
    if (displayName && !byDisplayName.has(displayName)) byDisplayName.set(displayName, participant)
  })
  return { byParticipantId, byUserId, byDisplayName }
}

export function resolveMeetingParticipant(
  index: MeetingParticipantIndex,
  participant: RemoteParticipantDescriptor,
) {
  let metadata: { participantId?: unknown; erpUserId?: unknown } = {}
  try {
    metadata = participant.metadata ? JSON.parse(participant.metadata) as typeof metadata : {}
  } catch {
    // Provider metadata is not authoritative enough to make the room fail.
  }

  const identityParts = participant.identity.split(':')
  const identityGuid = normalizeGuid(identityParts.length > 1 ? identityParts.at(-1) : participant.identity)
  const participantId = normalizeGuid(metadata.participantId)
  const erpUserId = normalizeGuid(metadata.erpUserId)
  return (participantId ? index.byParticipantId.get(participantId) : undefined)
    ?? (erpUserId ? index.byUserId.get(erpUserId) : undefined)
    ?? (participant.identity.startsWith('participant:') ? index.byParticipantId.get(identityGuid) : undefined)
    ?? (participant.identity.startsWith('guest:') ? index.byParticipantId.get(identityGuid) : undefined)
    ?? (participant.identity.startsWith('erp:') ? index.byUserId.get(identityGuid) : undefined)
    ?? index.byDisplayName.get(normalizeSearchValue(participant.displayName))
}

export function appendBoundedUnique<T extends { id: string }>(
  current: readonly T[],
  additions: readonly T[],
  limit: number,
) {
  const safeLimit = Math.max(0, Math.floor(limit))
  if (safeLimit === 0) return [] as T[]
  const seen = new Set<string>()
  const result: T[] = []
  for (const item of [...current, ...additions]) {
    if (seen.has(item.id)) continue
    seen.add(item.id)
    result.push(item)
  }
  return result.length > safeLimit ? result.slice(-safeLimit) : result
}

/** Makes an untrusted data-channel timestamp safe for Intl.DateTimeFormat. */
export function canonicalMeetingChatTimestamp(value: unknown, fallbackUtc: string) {
  const preferred = typeof value === 'string' ? Date.parse(value) : Number.NaN
  if (Number.isFinite(preferred)) return new Date(preferred).toISOString()
  const fallback = Date.parse(fallbackUtc)
  return Number.isFinite(fallback) ? new Date(fallback).toISOString() : new Date(0).toISOString()
}

/** Formats ephemeral chat time using the active ERP locale and Tehran timezone. */
export function formatMeetingChatTime(value: string, language: string) {
  const timestamp = Date.parse(value)
  if (!Number.isFinite(timestamp)) return '—'
  const locale = language.startsWith('fa') ? 'fa-IR-u-ca-persian' : 'en-GB'
  return new Intl.DateTimeFormat(locale, {
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZone: 'Asia/Tehran',
  }).format(new Date(timestamp))
}

export function canEmitWhiteboardUpdate(lastUpdateAtMs: number, nowMs: number) {
  return Number.isFinite(nowMs)
    && (lastUpdateAtMs === Number.NEGATIVE_INFINITY
      || (Number.isFinite(lastUpdateAtMs) && nowMs >= lastUpdateAtMs + WHITEBOARD_MIN_UPDATE_INTERVAL_MS))
}

export function connectionQualityPresentation(quality: PublicConnectionQuality): Readonly<{
  tone: MeetingTone
  labelKey: string
  guidanceKey: string
}> {
  if (quality === 'excellent') return { tone: 'positive', labelKey: 'collaborationMeetings.quality.excellent', guidanceKey: 'collaborationMeetings.qualityGuidance.excellent' }
  if (quality === 'good') return { tone: 'positive', labelKey: 'collaborationMeetings.quality.good', guidanceKey: 'collaborationMeetings.qualityGuidance.good' }
  if (quality === 'poor') return { tone: 'warning', labelKey: 'collaborationMeetings.quality.poor', guidanceKey: 'collaborationMeetings.qualityGuidance.poor' }
  if (quality === 'lost') return { tone: 'critical', labelKey: 'collaborationMeetings.quality.lost', guidanceKey: 'collaborationMeetings.qualityGuidance.lost' }
  return { tone: 'information', labelKey: 'collaborationMeetings.quality.unknown', guidanceKey: 'collaborationMeetings.qualityGuidance.unknown' }
}

export function resolveMeetingShortcut(input: MeetingShortcutInput): MeetingShortcut | null {
  if (!input.ctrlKey || input.altKey || input.metaKey || input.repeat || input.editableTarget) return null
  const key = input.key.toLocaleLowerCase()
  if (!input.shiftKey && key === 'd') return 'MICROPHONE'
  if (!input.shiftKey && key === 'e') return 'CAMERA'
  if (input.shiftKey && key === 's') return 'SCREEN_SHARE'
  return null
}

export function participantMatchesMeetingSearch(name: string, roleLabel: string, query: string) {
  const normalizedQuery = normalizeSearchValue(query)
  if (!normalizedQuery) return true
  return `${normalizeSearchValue(name)} ${normalizeSearchValue(roleLabel)}`.includes(normalizedQuery)
}
