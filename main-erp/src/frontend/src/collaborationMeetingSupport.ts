export const meetingStatuses = ['SCHEDULED', 'LIVE', 'ENDED', 'CANCELLED'] as const
export type MeetingStatus = typeof meetingStatuses[number]

export const meetingRoles = ['HOST', 'COHOST', 'PRESENTER', 'ATTENDEE', 'EXTERNAL_GUEST'] as const
export type MeetingRole = typeof meetingRoles[number]

export const meetingAdmissionStatuses = ['INVITED', 'WAITING', 'ADMITTED', 'DENIED', 'REVOKED'] as const
export type MeetingAdmissionStatus = typeof meetingAdmissionStatuses[number]

export type MeetingTone = 'positive' | 'critical' | 'warning' | 'information' | 'neutral'

export type MeetingPollingProfile = Readonly<{
  baseDelayMs: number
  maxDelayMs: number
  jitterRatio: number
}>

export const guestMeetingStatusPollingProfile: MeetingPollingProfile = {
  baseDelayMs: 10_000,
  maxDelayMs: 60_000,
  jitterRatio: 0.15,
}

export const activeMeetingDetailPollingProfile: MeetingPollingProfile = {
  baseDelayMs: 12_000,
  maxDelayMs: 60_000,
  jitterRatio: 0.15,
}

/** Returns a bounded exponential-backoff delay with deterministic injectable jitter. */
export function meetingPollingDelayMs(
  profile: MeetingPollingProfile,
  consecutiveFailures = 0,
  randomUnit = 0.5,
) {
  const failures = Number.isFinite(consecutiveFailures)
    ? Math.min(6, Math.max(0, Math.trunc(consecutiveFailures)))
    : 0
  const sample = Number.isFinite(randomUnit) ? Math.min(1, Math.max(0, randomUnit)) : 0.5
  const adaptiveDelay = Math.min(profile.maxDelayMs, profile.baseDelayMs * (2 ** failures))
  const jitter = adaptiveDelay * profile.jitterRatio
  return Math.max(250, Math.round(Math.min(
    profile.maxDelayMs,
    adaptiveDelay - jitter + (2 * jitter * sample),
  )))
}

export function meetingRequestHttpStatus(error: unknown) {
  if (!error || typeof error !== 'object' || !('status' in error)) return null
  const status = (error as { status?: unknown }).status
  return typeof status === 'number' && Number.isInteger(status) ? status : null
}

/** Only an authoritative guest-credential rejection may destroy a stored access ticket. */
export function shouldDiscardGuestMeetingTicket(error: unknown) {
  const status = meetingRequestHttpStatus(error)
  return status === 401 || status === 410
}

export type MeetingRequestScope = Readonly<{
  key: string | null
  generation: number
}>

/**
 * Prevents a late request from one meeting (or one activation of the same
 * meeting) from committing into the currently active call UI.
 */
export class MeetingRequestGenerationGate {
  private key: string | null = null
  private generation = 0
  private committedVersion: number | null = null

  transition(key: string | null, committedVersion: number | null = null): MeetingRequestScope {
    this.key = key
    this.generation += 1
    this.committedVersion = Number.isInteger(committedVersion) ? committedVersion : null
    return this.capture()
  }

  capture(): MeetingRequestScope {
    return { key: this.key, generation: this.generation }
  }

  currentKey() {
    return this.key
  }

  isCurrent(scope: MeetingRequestScope) {
    return scope.key === this.key && scope.generation === this.generation
  }

  tryCommitVersion(scope: MeetingRequestScope, responseKey: string, responseVersion: number) {
    if (!this.isCurrent(scope) || responseKey !== this.key || !Number.isInteger(responseVersion)) return false
    if (this.committedVersion !== null && responseVersion < this.committedVersion) return false
    this.committedVersion = responseVersion
    return true
  }
}

export type MeetingPolicy = {
  attendeeMicrophone: boolean
  attendeeCamera: boolean
  attendeeScreenShare: boolean
  guestMicrophone: boolean
  guestCamera: boolean
  guestScreenShare: boolean
  chatEnabled: boolean
  fileSharingEnabled: boolean
  ephemeralWhiteboardEnabled: boolean
  recordingEnabled: boolean
}

export type MeetingPolicyInput = Omit<MeetingPolicy, 'recordingEnabled'>

export type MeetingInternalParticipantInput = {
  userId: string
  role: Exclude<MeetingRole, 'HOST' | 'EXTERNAL_GUEST'>
}

export type CreateMeetingRequest = {
  title: string
  scheduledStartAtUtc?: string | null
  scheduledEndAtUtc?: string | null
  participants?: MeetingInternalParticipantInput[] | null
  policy?: MeetingPolicyInput | null
}

export type UpdateMeetingRequest = {
  title: string
  scheduledStartAtUtc: string
  scheduledEndAtUtc: string
  participants?: MeetingInternalParticipantInput[] | null
  policy?: MeetingPolicyInput | null
  expectedVersion: number
}

export type MeetingVersionRequest = { expectedVersion: number }
export type MeetingParticipantRoleRequest = {
  role: Exclude<MeetingRole, 'HOST' | 'EXTERNAL_GUEST'>
  expectedVersion: number
}
export type CreateMeetingGuestInviteRequest = { guestDisplayName: string; expiresAtUtc: string }
export type MeetingGuestTokenRequest = { inviteToken: string }
export type MeetingGuestTicketRequest = { accessTicket: string }

export type MeetingParticipant = {
  id: string
  userId?: string | null
  displayName: string
  role: MeetingRole
  admissionStatus: MeetingAdmissionStatus
  createdAtUtc: string
  admittedAtUtc?: string | null
  deniedAtUtc?: string | null
  revokedAtUtc?: string | null
  version: number
}

export type MeetingGuestParticipant = {
  id: string
  displayName: string
  role: MeetingRole
  admissionStatus: MeetingAdmissionStatus
}

export type MeetingRoomRosterParticipant = MeetingGuestParticipant & {
  userId?: string | null
}

export type MeetingSession = {
  id: string
  conversationId: string
  title: string
  status: MeetingStatus
  isLocked: boolean
  provider: string
  scheduledStartAtUtc?: string | null
  scheduledEndAtUtc?: string | null
  startedAtUtc?: string | null
  endedAtUtc?: string | null
  cancelledAtUtc?: string | null
  createdByUserId: string
  createdByDisplayName: string
  createdAtUtc: string
  version: number
  policy: MeetingPolicy
  participants: MeetingParticipant[]
}

export type MeetingListItem = {
  id: string
  conversationId: string
  title: string
  status: MeetingStatus
  isLocked: boolean
  scheduledStartAtUtc?: string | null
  scheduledEndAtUtc?: string | null
  startedAtUtc?: string | null
  endedAtUtc?: string | null
  participantCount: number
  waitingCount: number
  version: number
}
export type MeetingDetail = MeetingSession
export type MeetingListResponse = {
  items: MeetingListItem[]
  page: number
  pageSize: number
  totalCount: number
}

/**
 * An initial list failure is a distinct state from an empty meeting dataset.
 * A refresh failure with already-loaded rows keeps those rows visible as stale
 * context, while the page-level error offers an explicit retry.
 */
export function shouldShowMeetingListFailure(loading: boolean, error: string, itemCount: number) {
  return !loading && error.trim().length > 0 && itemCount === 0
}

export type MeetingJoinCapabilities = {
  microphone: boolean
  camera: boolean
  screenShare: boolean
  chat: boolean
  fileSharing: boolean
  ephemeralWhiteboard: boolean
  recording: boolean
}

export type MeetingJoinResponse = {
  meeting: MeetingDetail
  participant: MeetingParticipant
  serverUrl: string
  accessToken: string
  capabilities: MeetingJoinCapabilities
}

export type MeetingRoomJoin = {
  meeting: {
    title: string
    participants: readonly MeetingRoomRosterParticipant[]
  }
  participant: MeetingRoomRosterParticipant
  serverUrl: string
  accessToken: string
  capabilities: MeetingJoinCapabilities
}

export type MeetingGuestRoom = {
  title: string
  participants: MeetingGuestParticipant[]
}

export type MeetingGuestJoinResponse = {
  meeting: MeetingGuestRoom
  participant: MeetingGuestParticipant
  serverUrl: string
  accessToken: string
  capabilities: MeetingJoinCapabilities
}

export type MeetingGuestPreviewResponse = {
  meetingId: string
  title: string
  status: MeetingStatus
  isLocked: boolean
  scheduledStartAtUtc?: string | null
  scheduledEndAtUtc?: string | null
  policy: MeetingPolicy
}

export type MeetingGuestRedeemResponse = {
  accessTicket: string
  accessTicketExpiresAtUtc: string
  meeting: MeetingGuestPreviewResponse
  participant: MeetingGuestParticipant
}

export type MeetingGuestStatusResponse = {
  meeting: MeetingGuestPreviewResponse
  participant: MeetingGuestParticipant
  canJoin: boolean
}

export type MeetingGuestInviteResponse = {
  id: string
  meetingId: string
  participant: MeetingParticipant
  inviteToken: string
  expiresAtUtc: string
  createdAtUtc: string
}

export type MeetingAttendanceInterval = {
  id: string
  connectedAtUtc: string
  disconnectedAtUtc?: string | null
  source: 'CLIENT' | 'PROVIDER' | string
}

export type MeetingParticipantReport = {
  participant: MeetingParticipant
  attendanceIntervals: MeetingAttendanceInterval[]
  totalConnectedSeconds: number
}

export type MeetingEvent = {
  id: string
  eventType: string
  participantId?: string | null
  actorUserId?: string | null
  actorDisplayName: string
  source: 'UI' | 'CLIENT' | 'PROVIDER' | string
  occurredAtUtc: string
  detail?: string | null
}

export type MeetingReportResponse = {
  meeting: MeetingDetail
  participants: MeetingParticipantReport[]
  events: MeetingEvent[]
  generatedAtUtc: string
}

export type MeetingCreateMode = 'SCHEDULED' | 'INSTANT'

type MeetingCreateSubmitState = {
  busy: boolean
  title: string
  mode: MeetingCreateMode
  date: string
  resolvedDate: string
  dateError: string
}

const meetingCreateHeaderTranslationKeys: Record<MeetingCreateMode, { title: string; subtitle: string }> = {
  SCHEDULED: {
    title: 'collaborationMeetings.createScheduledTitle',
    subtitle: 'collaborationMeetings.createScheduledSubtitle',
  },
  INSTANT: {
    title: 'collaborationMeetings.createInstantTitle',
    subtitle: 'collaborationMeetings.createInstantSubtitle',
  },
}

export function meetingCreateHeaderKeys(mode: MeetingCreateMode) {
  return meetingCreateHeaderTranslationKeys[mode]
}

/**
 * Scheduled meetings cannot be submitted until their business date has been
 * resolved to a canonical Gregorian date. Instant meetings deliberately do not
 * depend on any date state left behind while switching modes.
 */
export function isMeetingCreateSubmitDisabled(state: MeetingCreateSubmitState) {
  if (state.busy || state.title.trim().length === 0) return true
  if (state.mode === 'INSTANT') return false
  return state.date.trim().length === 0
    || state.resolvedDate.trim().length === 0
    || state.dateError.trim().length > 0
}

const statusTranslationKeys: Record<MeetingStatus, string> = {
  SCHEDULED: 'collaborationMeetings.status.SCHEDULED',
  LIVE: 'collaborationMeetings.status.LIVE',
  ENDED: 'collaborationMeetings.status.ENDED',
  CANCELLED: 'collaborationMeetings.status.CANCELLED',
}

const roleTranslationKeys: Record<MeetingRole, string> = {
  HOST: 'collaborationMeetings.roles.HOST',
  COHOST: 'collaborationMeetings.roles.COHOST',
  PRESENTER: 'collaborationMeetings.roles.PRESENTER',
  ATTENDEE: 'collaborationMeetings.roles.ATTENDEE',
  EXTERNAL_GUEST: 'collaborationMeetings.roles.EXTERNAL_GUEST',
}

const admissionTranslationKeys: Record<MeetingAdmissionStatus, string> = {
  INVITED: 'collaborationMeetings.admission.INVITED',
  WAITING: 'collaborationMeetings.admission.WAITING',
  ADMITTED: 'collaborationMeetings.admission.ADMITTED',
  DENIED: 'collaborationMeetings.admission.DENIED',
  REVOKED: 'collaborationMeetings.admission.REVOKED',
}

export function isMeetingStatus(value: unknown): value is MeetingStatus {
  return typeof value === 'string' && (meetingStatuses as readonly string[]).includes(value)
}

export function isMeetingRole(value: unknown): value is MeetingRole {
  return typeof value === 'string' && (meetingRoles as readonly string[]).includes(value)
}

export function isMeetingAdmissionStatus(value: unknown): value is MeetingAdmissionStatus {
  return typeof value === 'string' && (meetingAdmissionStatuses as readonly string[]).includes(value)
}

export function meetingStatusTranslationKey(status: unknown) {
  return isMeetingStatus(status) ? statusTranslationKeys[status] : 'collaborationMeetings.status.UNKNOWN'
}

export function meetingRoleTranslationKey(role: unknown) {
  return isMeetingRole(role) ? roleTranslationKeys[role] : 'collaborationMeetings.roles.UNKNOWN'
}

export function meetingAdmissionTranslationKey(status: unknown) {
  return isMeetingAdmissionStatus(status)
    ? admissionTranslationKeys[status]
    : 'collaborationMeetings.admission.UNKNOWN'
}

export function meetingStatusTone(status: unknown): MeetingTone {
  switch (status) {
    case 'LIVE': return 'positive'
    case 'SCHEDULED': return 'information'
    case 'CANCELLED': return 'critical'
    default: return 'neutral'
  }
}

export function meetingRoleTone(role: unknown): MeetingTone {
  switch (role) {
    case 'HOST': return 'positive'
    case 'COHOST': return 'information'
    case 'PRESENTER': return 'warning'
    case 'EXTERNAL_GUEST': return 'information'
    default: return 'neutral'
  }
}

export function meetingAdmissionTone(status: unknown): MeetingTone {
  switch (status) {
    case 'ADMITTED': return 'positive'
    case 'WAITING': return 'warning'
    case 'DENIED':
    case 'REVOKED': return 'critical'
    case 'INVITED': return 'information'
    default: return 'neutral'
  }
}

export function meetingRequestErrorText(error: unknown, fallback: string, forbidden = fallback) {
  if (!(error instanceof Error)) return fallback
  const message = error.message.trim()
  if (!message || message === 'Meeting request failed.' || /failed to fetch|networkerror|load failed/i.test(message)) return fallback
  if (message === 'Forbidden') return forbidden
  return message
}

/**
 * Computes the local UI affordances from a server policy. The media provider token
 * remains authoritative. Unknown roles fail closed, and recording is deliberately
 * disabled in Meetings V1 even if an untrusted payload claims otherwise.
 */
export function meetingCapabilitiesForRole(role: unknown, policy: MeetingPolicy): MeetingJoinCapabilities {
  if (!isMeetingRole(role)) return noMeetingCapabilities()

  const common = {
    chat: policy.chatEnabled,
    fileSharing: policy.fileSharingEnabled,
    ephemeralWhiteboard: policy.ephemeralWhiteboardEnabled,
    recording: false as const,
  }

  if (role === 'HOST' || role === 'COHOST' || role === 'PRESENTER') {
    return { microphone: true, camera: true, screenShare: true, ...common }
  }
  if (role === 'ATTENDEE') {
    return {
      microphone: policy.attendeeMicrophone,
      camera: policy.attendeeCamera,
      screenShare: policy.attendeeScreenShare,
      ...common,
    }
  }
  return {
    microphone: policy.guestMicrophone,
    camera: policy.guestCamera,
    screenShare: policy.guestScreenShare,
    chat: policy.chatEnabled,
    fileSharing: false,
    ephemeralWhiteboard: policy.ephemeralWhiteboardEnabled,
    recording: false,
  }
}

function noMeetingCapabilities(): MeetingJoinCapabilities {
  return {
    microphone: false,
    camera: false,
    screenShare: false,
    chat: false,
    fileSharing: false,
    ephemeralWhiteboard: false,
    recording: false,
  }
}

export type MeetingLifecycleActions = {
  canEdit: boolean
  canStart: boolean
  canCancel: boolean
  canJoin: boolean
  canLock: boolean
  canUnlock: boolean
  canEnd: boolean
  canInviteGuest: boolean
  canManageParticipants: boolean
  canAdmitGuest: boolean
  canChangeRoles: boolean
  canEnterWaitingRoom: boolean
}

export function meetingLifecycleActions(
  meeting: Pick<MeetingSession, 'status' | 'isLocked'>,
  role: unknown,
  admissionStatus: unknown,
): MeetingLifecycleActions {
  const knownRole = isMeetingRole(role) ? role : null
  const knownAdmission = isMeetingAdmissionStatus(admissionStatus) ? admissionStatus : null
  const active = meeting.status === 'SCHEDULED' || meeting.status === 'LIVE'
  const manager = knownRole === 'HOST' || knownRole === 'COHOST'
  const participant = knownRole !== null && knownAdmission === 'ADMITTED'

  return {
    canEdit: meeting.status === 'SCHEDULED' && knownRole === 'HOST',
    canStart: meeting.status === 'SCHEDULED' && manager,
    canCancel: meeting.status === 'SCHEDULED' && manager,
    canJoin: meeting.status === 'LIVE' && participant,
    canLock: active && manager && !meeting.isLocked,
    canUnlock: active && manager && meeting.isLocked,
    canEnd: meeting.status === 'LIVE' && manager,
    canInviteGuest: active && manager && !meeting.isLocked,
    canManageParticipants: active && manager,
    canAdmitGuest: active && manager && !meeting.isLocked,
    canChangeRoles: active && knownRole === 'HOST',
    canEnterWaitingRoom: active && knownRole === 'EXTERNAL_GUEST'
      && knownAdmission === 'INVITED' && !meeting.isLocked,
  }
}

export const meetingListRoute = '/collaboration/meetings'
export const newMeetingRoute = '/collaboration/meetings/new'
export const guestMeetingRoute = '/meeting/guest'

const canonicalGuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const inviteTokenPattern = /^[A-Za-z0-9_-]{32,512}$/

export function buildMeetingDetailRoute(meetingId: string) {
  if (!canonicalGuidPattern.test(meetingId)) throw new TypeError('Invalid meeting id.')
  return `${meetingListRoute}/${meetingId.toLowerCase()}`
}

export function isValidMeetingInviteToken(value: unknown): value is string {
  return typeof value === 'string' && inviteTokenPattern.test(value)
}

export function buildGuestMeetingInviteRoute(inviteToken: string) {
  if (!isValidMeetingInviteToken(inviteToken)) throw new TypeError('Invalid meeting invite token.')
  return `${guestMeetingRoute}#${new URLSearchParams({ invite: inviteToken }).toString()}`
}

function normalizePublicAppOrigin(value: string | undefined) {
  if (!value?.trim()) return null
  try {
    const url = new URL(value.trim())
    if (!['http:', 'https:'].includes(url.protocol)
      || url.username
      || url.password
      || url.pathname !== '/'
      || url.search
      || url.hash) return null
    return url.origin
  } catch {
    return null
  }
}

/**
 * Builds a shareable guest URL from the canonical frontend origin. A configured
 * origin lets a host browsing through localhost copy the LAN/HTTPS address;
 * malformed overrides fail safely back to the origin serving the current page.
 */
export function buildGuestMeetingInviteUrl(
  inviteToken: string,
  browserOrigin: string,
  configuredPublicOrigin?: string,
) {
  const origin = normalizePublicAppOrigin(configuredPublicOrigin)
    ?? normalizePublicAppOrigin(browserOrigin)
  if (!origin) throw new TypeError('Invalid application origin.')
  return `${origin}${buildGuestMeetingInviteRoute(inviteToken)}`
}

/** Reads a one-time invitation only from the URL fragment, never from query parameters. */
export function extractMeetingInviteToken(value: string | URL) {
  let url: URL
  try {
    url = value instanceof URL ? value : new URL(value, 'https://hadron.invalid')
  } catch {
    return null
  }

  if (!url.hash.startsWith('#')) return null
  const params = new URLSearchParams(url.hash.slice(1))
  const tokens = params.getAll('invite')
  if (tokens.length !== 1 || !isValidMeetingInviteToken(tokens[0])) return null
  return tokens[0]
}

export function isGuestAccessTicketExpired(expiresAtUtc: string, nowUtc: string | number | Date = Date.now()) {
  const expiresAt = parseUtcInstant(expiresAtUtc)
  const now = typeof nowUtc === 'number' ? nowUtc : nowUtc instanceof Date
    ? nowUtc.getTime()
    : parseUtcInstant(nowUtc)
  if (expiresAt === null || now === null || !Number.isFinite(now)) return true
  return expiresAt <= now
}

const digitMap: Record<string, string> = {
  '۰': '0', '۱': '1', '۲': '2', '۳': '3', '۴': '4',
  '۵': '5', '۶': '6', '۷': '7', '۸': '8', '۹': '9',
  '٠': '0', '١': '1', '٢': '2', '٣': '3', '٤': '4',
  '٥': '5', '٦': '6', '٧': '7', '٨': '8', '٩': '9',
}

function asciiDigits(value: string) {
  return value.replace(/[۰-۹٠-٩]/g, digit => digitMap[digit] ?? digit)
}

/** Normalizes a Gregorian wall-clock value for an HTML datetime-local control. */
export function normalizeMeetingLocalDateTimeInput(value: string) {
  const normalized = asciiDigits(value.trim())
    .replace(/\//g, '-')
    .replace(/\s+/, 'T')
  return parseLocalDateTime(normalized) ? normalized : null
}

/** Converts a timezone-less business wall clock to a canonical UTC instant. */
export function meetingLocalDateTimeToUtc(value: string, timeZone = 'Asia/Tehran') {
  const normalized = normalizeMeetingLocalDateTimeInput(value)
  if (!normalized || !isSupportedTimeZone(timeZone)) return null
  const target = parseLocalDateTime(normalized)
  if (!target) return null

  const targetWallTime = Date.UTC(target.year, target.month - 1, target.day, target.hour, target.minute)
  let candidate = targetWallTime
  for (let attempt = 0; attempt < 4; attempt++) {
    const current = zonedParts(candidate, timeZone)
    if (!current) return null
    const currentWallTime = Date.UTC(current.year, current.month - 1, current.day, current.hour, current.minute)
    const correction = targetWallTime - currentWallTime
    candidate += correction
    if (correction === 0) break
  }

  const verified = zonedParts(candidate, timeZone)
  if (!verified || !sameLocalDateTime(target, verified)) return null
  return new Date(candidate).toISOString()
}

/** Converts an explicit UTC/offset instant to a datetime-local value in the requested zone. */
export function meetingUtcToLocalDateTime(value: string, timeZone = 'Asia/Tehran') {
  const timestamp = parseUtcInstant(value)
  if (timestamp === null || !isSupportedTimeZone(timeZone)) return null
  const parts = zonedParts(timestamp, timeZone)
  if (!parts) return null
  return `${four(parts.year)}-${two(parts.month)}-${two(parts.day)}T${two(parts.hour)}:${two(parts.minute)}`
}

type LocalDateTimeParts = { year: number; month: number; day: number; hour: number; minute: number }

function parseLocalDateTime(value: string): LocalDateTimeParts | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value)
  if (!match) return null
  const parts = {
    year: Number(match[1]),
    month: Number(match[2]),
    day: Number(match[3]),
    hour: Number(match[4]),
    minute: Number(match[5]),
  }
  if (parts.year < 1 || parts.year > 9999 || parts.month < 1 || parts.month > 12
    || parts.day < 1 || parts.day > 31 || parts.hour > 23 || parts.minute > 59) return null
  const check = new Date(Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute))
  if (check.getUTCFullYear() !== parts.year || check.getUTCMonth() !== parts.month - 1
    || check.getUTCDate() !== parts.day || check.getUTCHours() !== parts.hour
    || check.getUTCMinutes() !== parts.minute) return null
  return parts
}

function parseUtcInstant(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$/i.exec(value)
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const hour = Number(match[4])
  const minute = Number(match[5])
  const second = Number(match[6])
  if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 59) return null
  const dateCheck = new Date(Date.UTC(year, month - 1, day))
  if (dateCheck.getUTCFullYear() !== year || dateCheck.getUTCMonth() !== month - 1 || dateCheck.getUTCDate() !== day) return null
  if (match[7] !== 'Z' && match[7] !== 'z') {
    const [offsetHour, offsetMinute] = match[7].slice(1).split(':').map(Number)
    if (offsetHour > 23 || offsetMinute > 59) return null
  }
  const timestamp = Date.parse(value)
  return Number.isFinite(timestamp) ? timestamp : null
}

function isSupportedTimeZone(timeZone: string) {
  try {
    new Intl.DateTimeFormat('en-CA', { timeZone }).format(0)
    return true
  } catch {
    return false
  }
}

function zonedParts(timestamp: number, timeZone: string): LocalDateTimeParts | null {
  try {
    const values = new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
    }).formatToParts(new Date(timestamp))
    const read = (type: Intl.DateTimeFormatPartTypes) => Number(values.find(part => part.type === type)?.value)
    const parts = { year: read('year'), month: read('month'), day: read('day'), hour: read('hour'), minute: read('minute') }
    return Object.values(parts).every(Number.isFinite) ? parts : null
  } catch {
    return null
  }
}

function sameLocalDateTime(left: LocalDateTimeParts, right: LocalDateTimeParts) {
  return left.year === right.year && left.month === right.month && left.day === right.day
    && left.hour === right.hour && left.minute === right.minute
}

function two(value: number) { return String(value).padStart(2, '0') }
function four(value: number) { return String(value).padStart(4, '0') }
